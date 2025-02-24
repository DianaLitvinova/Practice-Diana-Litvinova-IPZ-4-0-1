using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Npgsql;
using Diana_Litvinova_IPZ_4_0_1.Models;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;

namespace Diana_Litvinova_IPZ_4_0_1.Controllers
{
    public class HeadTeacherController : Controller
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private string _connectionString => _httpContextAccessor.HttpContext?.Session.GetString("ConnectionString");

        public HeadTeacherController(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public IActionResult Index()
        {
            return View();
        }

        public async Task<IActionResult> Employees()
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                var employees = await GetEmployeesList(conn);
                ViewBag.Posts = await GetPosts(conn);
                return View(employees);
            }
        }

        private async Task<List<EmployeeViewModel>> GetEmployeesList(NpgsqlConnection conn)
        {
            var employees = new List<EmployeeViewModel>();
            using (var cmd = new NpgsqlCommand(@"
        SELECT e.id, e.fullname, e.phone, p.postTitle, s.salary, ep.dateStart, ep.dateFinal
        FROM Employee e
        LEFT JOIN EmployeeAndPost ep ON e.id = ep.id_employee
        LEFT JOIN Post p ON ep.id_post = p.id
        LEFT JOIN Salary s ON p.id = s.id_post AND s.dateFinal IS NULL
        ORDER BY ep.dateFinal NULLS FIRST", conn))
            {
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    employees.Add(new EmployeeViewModel
                    {
                        Id = reader.GetInt16(0),
                        FullName = reader.GetString(1),
                        Phone = reader.GetString(2),
                        Post = !reader.IsDBNull(3) ? reader.GetString(3) : "",
                        Salary = !reader.IsDBNull(4) ? reader.GetDecimal(4) : 0,
                        DateStart = !reader.IsDBNull(5) ? reader.GetDateTime(5) : null,
                        DateFinal = !reader.IsDBNull(6) ? reader.GetDateTime(6) : null
                    });
                }
            }
            return employees;
        }

        private async Task<List<PostViewModel>> GetPosts(NpgsqlConnection conn)
        {
            var posts = new List<PostViewModel>();
            using (var cmd = new NpgsqlCommand(@"
               SELECT p.id, p.postTitle, s.salary 
               FROM Post p
               LEFT JOIN Salary s ON p.id = s.id_post 
               WHERE s.dateFinal IS NULL", conn))
            {
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    posts.Add(new PostViewModel
                    {
                        Id = reader.GetInt16(0),
                        Title = reader.GetString(1),
                        Salary = !reader.IsDBNull(2) ? reader.GetDecimal(2) : 0
                    });
                }
            }
            return posts;
        }

        public async Task<IActionResult> AddEmployee()
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                var posts = await GetPosts(conn);
                ViewBag.Posts = new SelectList(posts, "Id", "Title");
                return View();
            }
        }

        [HttpPost]
        public async Task<IActionResult> AddEmployee(EmployeeViewModel model)
        {
            if (ModelState.IsValid)
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using var transaction = await conn.BeginTransactionAsync();

                    try
                    {
                        // Проверяем, не существует ли уже такой пользователь
                        using (var checkCmd = new NpgsqlCommand(
                            $"SELECT COUNT(*) FROM pg_user WHERE usename = '{model.Username}'", conn))
                        {
                            var exists = (long)await checkCmd.ExecuteScalarAsync() > 0;
                            if (exists)
                            {
                                ModelState.AddModelError("Username", "Користувач з таким ім'ям вже існує");
                                return View(model);
                            }
                        }

                        // Добавляем сотрудника
                        Int16 employeeId;
                        using (var cmd = new NpgsqlCommand(
                            "INSERT INTO Employee (fullname, phone) VALUES (@fullname, @phone) RETURNING id", conn))
                        {
                            cmd.Parameters.AddWithValue("fullname", model.FullName);
                            cmd.Parameters.AddWithValue("phone", model.Phone);
                            employeeId = (Int16)await cmd.ExecuteScalarAsync();
                        }

                        // Добавляем связь с должностью
                        if (!string.IsNullOrEmpty(model.Post))
                        {
                            using var postCmd = new NpgsqlCommand(
                                "INSERT INTO EmployeeAndPost (id_employee, id_post) VALUES (@empId, @postId)", conn);
                            postCmd.Parameters.AddWithValue("empId", employeeId);
                            postCmd.Parameters.AddWithValue("postId", Int16.Parse(model.Post));
                            await postCmd.ExecuteNonQueryAsync();
                        }

                        // Создаем пользователя в PostgreSQL
                        string role = model.Post switch
                        {
                            "1" => "head_teacher",
                            "2" => "teacher",
                            "3" => "cook",
                            _ => throw new Exception("Невідома посада")
                        };

                        using (var userCmd = new NpgsqlCommand(
                            $"CREATE USER \"{model.Username}\" WITH PASSWORD '{model.Password}';", conn))
                        {
                            await userCmd.ExecuteNonQueryAsync();
                        }

                        using (var roleCmd = new NpgsqlCommand(
                            $"GRANT {role} TO \"{model.Username}\";", conn))
                        {
                            await roleCmd.ExecuteNonQueryAsync();
                        }

                        await transaction.CommitAsync();
                        return RedirectToAction(nameof(Employees));
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        ModelState.AddModelError("", $"Помилка при додаванні співробітника: {ex.Message}");
                    }
                }
            }

            // При ошибке возвращаем форму с сообщением
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                var posts = await GetPosts(conn);
                ViewBag.Posts = new SelectList(posts, "Id", "Title");
                return View(model);
            }
        }

        [HttpPost]
        public async Task<IActionResult> UpdateEmployee([FromBody] EmployeeViewModel model)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using var transaction = await conn.BeginTransactionAsync();

                try
                {
                    // Обновляем основную информацию
                    using (var cmd = new NpgsqlCommand(
                        "UPDATE Employee SET fullname = @fullname, phone = @phone WHERE id = @id", conn))
                    {
                        cmd.Parameters.AddWithValue("id", model.Id);
                        cmd.Parameters.AddWithValue("fullname", model.FullName);
                        cmd.Parameters.AddWithValue("phone", model.Phone);
                        await cmd.ExecuteNonQueryAsync();
                    }

                    // Обновляем должность
                    if (!string.IsNullOrEmpty(model.Post))
                    {
                        using (var cmd = new NpgsqlCommand(
                            @"UPDATE EmployeeAndPost 
                           SET dateFinal = CURRENT_DATE 
                           WHERE id_employee = @id AND dateFinal IS NULL", conn))
                        {
                            cmd.Parameters.AddWithValue("id", model.Id);
                            await cmd.ExecuteNonQueryAsync();
                        }

                        using (var cmd = new NpgsqlCommand(
                            "INSERT INTO EmployeeAndPost (id_employee, id_post) VALUES (@empId, @postId)", conn))
                        {
                            cmd.Parameters.AddWithValue("empId", model.Id);
                            cmd.Parameters.AddWithValue("postId", int.Parse(model.Post));
                            await cmd.ExecuteNonQueryAsync();
                        }
                    }

                    await transaction.CommitAsync();
                    return Json(new { success = true });
                }
                catch
                {
                    await transaction.RollbackAsync();
                    return Json(new { success = false });
                }
            }
        }

        [HttpPost]
        public async Task<IActionResult> DismissEmployee(int id)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using var cmd = new NpgsqlCommand(@"
            UPDATE EmployeeAndPost 
            SET dateFinal = CURRENT_DATE 
            WHERE id_employee = @id 
            AND dateFinal IS NULL", conn);

                cmd.Parameters.AddWithValue("id", id);
                await cmd.ExecuteNonQueryAsync();
                return Json(new { success = true });
            }
        }

        [HttpPost]
        public async Task<IActionResult> DeleteEmployee(int id)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                // Проверка связей в Invoice
                using (var cmd = new NpgsqlCommand(@"
           SELECT 
               CASE 
                   WHEN EXISTS(SELECT 1 FROM Invoice WHERE ID_cook = @id) THEN 'INVOICE'
                   WHEN EXISTS(SELECT 1 FROM PaymentReportFamily WHERE ID_head = @id) THEN 'PAYMENT' 
                   ELSE NULL 
               END", conn))
                {
                    cmd.Parameters.AddWithValue("id", id);
                    var restrictionType = await cmd.ExecuteScalarAsync() as string;

                    if (restrictionType != null)
                    {
                        string message = restrictionType == "INVOICE"
                            ? "Неможливо видалити співробітника, оскільки він має відношення до накладних. " +
                              "Для видалення цього запису необхідні права адміністратора бази даних."
                            : "Неможливо видалити співробітника, оскільки він має відношення до фінансових операцій. " +
                              "Для видалення необхідно спочатку видалити всі пов'язані фінансові записи.";

                        return Json(new { success = false, message = message });
                    }
                }

                // Проверка активных групп для воспитателя
                using (var cmd = new NpgsqlCommand(
                    "SELECT EXISTS(SELECT 1 FROM ChildrensGroup WHERE ID_teacher = @id)", conn))
                {
                    cmd.Parameters.AddWithValue("id", id);
                    if ((bool)await cmd.ExecuteScalarAsync())
                    {
                        return Json(new
                        {
                            success = false,
                            message = "Неможливо видалити співробітника, оскільки він є вихователем активної групи. " +
                                     "Спочатку призначте іншого вихователя для групи."
                        });
                    }
                }

                using var transaction = await conn.BeginTransactionAsync();
                try
                {
                    // Удаляем записи в правильном порядке
                    var queries = new[]
                    {
               "DELETE FROM BonusAndFine WHERE ID_employeeAndPost IN (SELECT ID FROM EmployeeAndPost WHERE ID_employee = @id)",
               "DELETE FROM EmployeeAndPost WHERE ID_employee = @id",
               "DELETE FROM Employee WHERE ID = @id"
           };

                    foreach (var query in queries)
                    {
                        using var cmd = new NpgsqlCommand(query, conn);
                        cmd.Parameters.AddWithValue("id", id);
                        await cmd.ExecuteNonQueryAsync();
                    }

                    await transaction.CommitAsync();
                    return Json(new { success = true });
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync();
                    return Json(new
                    {
                        success = false,
                        message = "Помилка при видаленні співробітника. Зверніться до адміністратора системи."
                    });
                }
            }
        }

        [HttpPost]
        public async Task<IActionResult> AddBonus([FromBody] BonusViewModel model)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using var transaction = await conn.BeginTransactionAsync();

                try
                {
                    // Получаем текущую должность сотрудника
                    int employeeAndPostId;
                    using (var cmd = new NpgsqlCommand(@"
                SELECT id FROM EmployeeAndPost 
                WHERE id_employee = @employeeId 
                AND dateFinal IS NULL", conn))
                    {
                        cmd.Parameters.AddWithValue("employeeId", model.EmployeeId);
                        employeeAndPostId = (int)await cmd.ExecuteScalarAsync();
                    }

                    // Добавляем премию/штраф
                    using (var cmd = new NpgsqlCommand(@"
                INSERT INTO BonusAndFine (id_employeeAndPost, amountOfMoney, description)
                VALUES (@empPostId, @amount, @description)", conn))
                    {
                        cmd.Parameters.AddWithValue("empPostId", employeeAndPostId);
                        cmd.Parameters.AddWithValue("amount", model.Amount);
                        cmd.Parameters.AddWithValue("description", model.Description);
                        await cmd.ExecuteNonQueryAsync();
                    }

                    await transaction.CommitAsync();
                    return Json(new { success = true });
                }
                catch
                {
                    await transaction.RollbackAsync();
                    return Json(new { success = false });
                }
            }
        }

        public async Task<IActionResult> EmployeeDetails(int id)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                var model = new EmployeeDetailsViewModel();

                // Получаем основную информацию
                using (var cmd = new NpgsqlCommand(@"
           WITH CurrentPost AS (
               SELECT ep.id_employee, p.postTitle, s.salary, ep.dateStart, ep.dateFinal
               FROM EmployeeAndPost ep
               JOIN Post p ON ep.id_post = p.id
               LEFT JOIN Salary s ON p.id = s.id_post AND s.dateFinal IS NULL
               WHERE ep.id_employee = @id AND ep.dateFinal IS NULL
           ),
           FirstAndLastDates AS (
               SELECT 
                   MIN(dateStart) as first_date,
                   MAX(dateFinal) as last_date
               FROM EmployeeAndPost
               WHERE id_employee = @id
           )
           SELECT 
               e.id, e.fullname, e.phone,
               cp.postTitle, cp.salary, cp.dateStart, cp.dateFinal,
               fd.first_date, fd.last_date
           FROM Employee e
           LEFT JOIN CurrentPost cp ON e.id = cp.id_employee
           CROSS JOIN FirstAndLastDates fd
           WHERE e.id = @id", conn))
                {
                    cmd.Parameters.AddWithValue("id", id);
                    using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        model.Id = reader.GetInt32(0);
                        model.FullName = reader.GetString(1);
                        model.Phone = reader.GetString(2);
                        model.CurrentPost = !reader.IsDBNull(3) ? reader.GetString(3) : "";
                        model.CurrentSalary = !reader.IsDBNull(4) ? reader.GetDecimal(4) : 0;
                        model.DateStart = !reader.IsDBNull(7) ? reader.GetDateTime(7) : null;
                        model.DateFinal = !reader.IsDBNull(8) ? reader.GetDateTime(8) : null;
                    }
                }

                // Остальной код без изменений
                // Получаем историю должностей
                using (var cmd = new NpgsqlCommand(@"
           SELECT p.postTitle, ep.dateStart, ep.dateFinal
           FROM EmployeeAndPost ep
           JOIN Post p ON ep.id_post = p.id
           WHERE ep.id_employee = @id
           ORDER BY ep.dateStart DESC", conn))
                {
                    cmd.Parameters.AddWithValue("id", id);
                    using var reader = await cmd.ExecuteReaderAsync();
                    model.PostHistory = new List<EmployeePostHistory>();
                    while (await reader.ReadAsync())
                    {
                        model.PostHistory.Add(new EmployeePostHistory
                        {
                            Post = reader.GetString(0),
                            DateStart = reader.GetDateTime(1),
                            DateFinal = !reader.IsDBNull(2) ? reader.GetDateTime(2) : null
                        });
                    }
                }

                // Получаем премии и штрафы
                using (var cmd = new NpgsqlCommand(@"
           SELECT bf.amountOfMoney, bf.description, bf.dateAdd
           FROM BonusAndFine bf
           JOIN EmployeeAndPost ep ON bf.id_employeeAndPost = ep.id
           WHERE ep.id_employee = @id
           ORDER BY bf.dateAdd DESC", conn))
                {
                    cmd.Parameters.AddWithValue("id", id);
                    using var reader = await cmd.ExecuteReaderAsync();
                    model.BonusAndFines = new List<BonusAndFineRecord>();
                    while (await reader.ReadAsync())
                    {
                        model.BonusAndFines.Add(new BonusAndFineRecord
                        {
                            Amount = reader.GetDecimal(0),
                            Description = reader.GetString(1),
                            DateAdd = reader.GetDateTime(2)
                        });
                    }
                }

                return View(model);
            }
        }

        // Просмотр накладных (1.5)
        public async Task<IActionResult> Invoices()
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using var cmd = new NpgsqlCommand(@"
            SELECT 
                i.id,
                cook.fullname as cook_name,
                head.fullname as head_name,
                receiver.fullname as receiver_name,
                i.dateCreate,
                i.dateAccept,
                i.dateReceipt,
                i.statusInvoice,
                SUM(il.cost * il.actualAmount) as total_amount
            FROM Invoice i
            JOIN Employee cook ON i.ID_cook = cook.id
            LEFT JOIN Employee head ON i.ID_head = head.id
            LEFT JOIN Employee receiver ON i.ID_receiver = receiver.id
            LEFT JOIN InvoiceList il ON i.id = il.ID_invoice
            GROUP BY i.id, cook.fullname, head.fullname, receiver.fullname,
                     i.dateCreate, i.dateAccept, i.dateReceipt, i.statusInvoice
            ORDER BY i.dateCreate DESC", conn);

                var invoices = new List<InvoiceViewModel>();
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    invoices.Add(new InvoiceViewModel
                    {
                        Id = reader.GetInt32(0),
                        CookName = reader.GetString(1),
                        HeadTeacherName = !reader.IsDBNull(2) ? reader.GetString(2) : null,
                        ReceiverName = !reader.IsDBNull(3) ? reader.GetString(3) : null,
                        DateCreate = reader.GetDateTime(4),
                        DateAccept = !reader.IsDBNull(5) ? reader.GetDateTime(5) : null,
                        DateReceipt = !reader.IsDBNull(6) ? reader.GetDateTime(6) : null,
                        Status = reader.GetString(7),
                        TotalAmount = !reader.IsDBNull(8) ? reader.GetDecimal(8) : 0
                    });
                }
                return View(invoices);
            }
        }

        // Детали накладной (1.7)
        public async Task<IActionResult> InvoiceDetails(int id)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                var invoice = await GetInvoiceWithProducts(conn, id);
                return View(invoice);
            }
        }

        private async Task<InvoiceViewModel> GetInvoiceWithProducts(NpgsqlConnection conn, int id)
        {
            var invoice = new InvoiceViewModel();

            // Получаем основную информацию о накладной
            using (var cmd = new NpgsqlCommand(@"
        SELECT 
            i.id,
            cook.fullname as cook_name,
            head.fullname as head_name,
            receiver.fullname as receiver_name,
            i.dateCreate,
            i.dateAccept,
            i.dateReceipt,
            i.statusInvoice
        FROM Invoice i
        JOIN Employee cook ON i.ID_cook = cook.id
        LEFT JOIN Employee head ON i.ID_head = head.id
        LEFT JOIN Employee receiver ON i.ID_receiver = receiver.id
        WHERE i.id = @id", conn))
            {
                cmd.Parameters.AddWithValue("id", id);
                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    invoice.Id = reader.GetInt32(0);
                    invoice.CookName = reader.GetString(1);
                    invoice.HeadTeacherName = !reader.IsDBNull(2) ? reader.GetString(2) : null;
                    invoice.ReceiverName = !reader.IsDBNull(3) ? reader.GetString(3) : null;
                    invoice.DateCreate = reader.GetDateTime(4);
                    invoice.DateAccept = !reader.IsDBNull(5) ? reader.GetDateTime(5) : null;
                    invoice.DateReceipt = !reader.IsDBNull(6) ? reader.GetDateTime(6) : null;
                    invoice.Status = reader.GetString(7);
                }
            }

            // Получаем список продуктов
            using (var cmd = new NpgsqlCommand(@"
        SELECT 
            p.id,
            p.nameProduct,
            il.measure,
            il.amount,
            il.actualAmount,
            il.cost
        FROM InvoiceList il
        JOIN Products p ON il.ID_products = p.id
        WHERE il.ID_invoice = @id", conn))
            {
                cmd.Parameters.AddWithValue("id", id);
                using var reader = await cmd.ExecuteReaderAsync();
                invoice.Products = new List<InvoiceProductViewModel>();
                while (await reader.ReadAsync())
                {
                    invoice.Products.Add(new InvoiceProductViewModel
                    {
                        ProductId = reader.GetInt32(0),
                        ProductName = reader.GetString(1),
                        Measure = reader.GetString(2),
                        Amount = reader.GetDecimal(3),
                        ActualAmount = !reader.IsDBNull(4) ? reader.GetDecimal(4) : null,
                        Cost = !reader.IsDBNull(5) ? reader.GetDecimal(5) : null
                    });
                }
            }

            invoice.TotalAmount = invoice.Products.Sum(p => (p.ActualAmount ?? 0) * (p.Cost ?? 0));

            return invoice;
        }
        [HttpPost]
        public async Task<IActionResult> ApproveInvoice(int id)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using var transaction = await conn.BeginTransactionAsync();

                try
                {
                    // Получаем имя пользователя из сессии
                    var username = HttpContext.Session.GetString("Username");

                    using (var cmd = new NpgsqlCommand(@"
                UPDATE Invoice 
                SET statusInvoice = 'Ухвалено', 
                    dateAccept = CURRENT_TIMESTAMP, 
                    ID_head = (SELECT ID FROM Employee WHERE fullname = @username)
                WHERE ID = @id AND statusInvoice = 'Перевіряється'", conn))
                    {
                        cmd.Parameters.AddWithValue("id", id);
                        cmd.Parameters.AddWithValue("username", username);
                        await cmd.ExecuteNonQueryAsync();
                    }

                    await transaction.CommitAsync();
                    return Json(new { success = true });
                }
                catch
                {
                    await transaction.RollbackAsync();
                    return Json(new { success = false });
                }
            }
        }

        [HttpPost]
        public async Task<IActionResult> RejectInvoice(int id)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using var transaction = await conn.BeginTransactionAsync();

                try
                {
                    // Получаем имя пользователя из сессии
                    var username = HttpContext.Session.GetString("Username");

                    using (var cmd = new NpgsqlCommand(@"
                UPDATE Invoice 
                SET statusInvoice = 'Відхилено', 
                    dateAccept = CURRENT_TIMESTAMP,
                    ID_head = (SELECT ID FROM Employee WHERE fullname = @username)
                WHERE ID = @id AND statusInvoice = 'Перевіряється'", conn))
                    {
                        cmd.Parameters.AddWithValue("id", id);
                        cmd.Parameters.AddWithValue("username", username);
                        await cmd.ExecuteNonQueryAsync();
                    }

                    await transaction.CommitAsync();
                    return Json(new { success = true });
                }
                catch
                {
                    await transaction.RollbackAsync();
                    return Json(new { success = false });
                }
            }
        }

        public async Task<IActionResult> ManageProducts()
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using var cmd = new NpgsqlCommand("SELECT id, nameProduct FROM Products ORDER BY nameProduct", conn);

                var products = new List<ProductViewModel>();
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    products.Add(new ProductViewModel
                    {
                        Id = reader.GetInt32(0),
                        Name = reader.GetString(1)
                    });
                }
                return View(products);
            }
        }

        [HttpPost]
        public async Task<IActionResult> AddProduct(ProductViewModel model)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using var cmd = new NpgsqlCommand(
                    "INSERT INTO Products (nameProduct) VALUES (@name)", conn);
                cmd.Parameters.AddWithValue("name", model.Name);
                await cmd.ExecuteNonQueryAsync();
                return RedirectToAction(nameof(ManageProducts));
            }
        }

        [HttpPost]
        public async Task<IActionResult> UpdateProduct(ProductViewModel model)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using var cmd = new NpgsqlCommand(
                    "UPDATE Products SET nameProduct = @name WHERE id = @id", conn);
                cmd.Parameters.AddWithValue("id", model.Id);
                cmd.Parameters.AddWithValue("name", model.Name);
                await cmd.ExecuteNonQueryAsync();
                return Json(new { success = true });
            }
        }

        [HttpPost]
        public async Task<IActionResult> DeleteProduct(int id)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                // Проверяем использование продукта
                using (var checkCmd = new NpgsqlCommand(
                    "SELECT EXISTS(SELECT 1 FROM InvoiceList WHERE ID_products = @id)", conn))
                {
                    checkCmd.Parameters.AddWithValue("id", id);
                    if ((bool)await checkCmd.ExecuteScalarAsync())
                    {
                        return Json(new
                        {
                            success = false,
                            message = "Неможливо видалити товар, оскільки він використовується в накладних"
                        });
                    }
                }

                using var cmd = new NpgsqlCommand("DELETE FROM Products WHERE id = @id", conn);
                cmd.Parameters.AddWithValue("id", id);
                await cmd.ExecuteNonQueryAsync();
                return Json(new { success = true });
            }
        }

        [HttpGet]
        public async Task<IActionResult> CreateInvoice()
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                // Получаем список товаров
                var products = await GetAllProducts(conn);
                ViewBag.Products = products;

                return View(new CreateInvoiceViewModel());
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreateInvoicePost(CreateInvoiceViewModel model)
        {
            if (!ModelState.IsValid || model.Items == null || !model.Items.Any())
            {
                ModelState.AddModelError("", "Необхідно додати хоча б один товар");
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    var products = await GetAllProducts(conn);
                    ViewBag.Products = products;
                    return View("CreateInvoice", model);
                }
            }
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using var transaction = await conn.BeginTransactionAsync();
                try
                {
                    // Получаем ID текущего пользователя из базы данных по имени из сессии
                    var username = HttpContext.Session.GetString("Username");
                    using var cmdCook = new NpgsqlCommand(
                        "SELECT ID FROM Employee WHERE fullname = @username",
                        conn,
                        transaction);
                    cmdCook.Parameters.AddWithValue("username", username);
                    var cookId = (short)await cmdCook.ExecuteScalarAsync();

                    // Создаем накладную
                    int invoiceId;
                    using (var cmd = new NpgsqlCommand(@"
                INSERT INTO Invoice (ID_cook, statusInvoice)
                VALUES (@cookId, 'Перевіряється')
                RETURNING id", conn, transaction))  // Добавлен transaction
                    {
                        cmd.Parameters.AddWithValue("cookId", cookId);  // Используем cookId вместо currentUserId
                        invoiceId = (int)await cmd.ExecuteScalarAsync();

                        // Добавляем товары
                        foreach (var item in model.Items)
                        {
                            using (var cmdItems = new NpgsqlCommand(@"
                          INSERT INTO InvoiceList (ID_invoice, ID_products, measure, amount)
                           VALUES (@invoiceId, @productId, @measure::measuretype, @amount)", conn, transaction))  // Добавлен transaction
                            {
                                cmdItems.Parameters.AddWithValue("invoiceId", invoiceId);
                                cmdItems.Parameters.AddWithValue("productId", item.ProductId);
                                cmdItems.Parameters.AddWithValue("measure", item.Measure);
                                cmdItems.Parameters.AddWithValue("amount", item.Amount);
                                await cmdItems.ExecuteNonQueryAsync();
                            }
                        }
                    }
                    await transaction.CommitAsync();
                    return RedirectToAction(nameof(Invoices));
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    ModelState.AddModelError("", $"Помилка при створенні накладної: {ex.Message}");
                    var products = await GetAllProducts(conn);
                    ViewBag.Products = products;
                    return View("CreateInvoice", model);
                }
            }
        }

        private async Task<List<EmployeeViewModel>> GetCooks(NpgsqlConnection conn)
        {
            var cooks = new List<EmployeeViewModel>();
            using var cmd = new NpgsqlCommand(@"
        SELECT DISTINCT e.id, e.fullname 
        FROM Employee e
        JOIN EmployeeAndPost ep ON e.id = ep.id_employee
        JOIN Post p ON ep.id_post = p.id
        WHERE p.postTitle = 'Кухар'
        AND ep.dateFinal IS NULL
        AND NOT EXISTS (
            SELECT 1 FROM EmployeeAndPost ep2
            JOIN Post p2 ON ep2.id_post = p2.id
            WHERE ep2.id_employee = e.id
            AND p2.postTitle = 'Завідуючий'
            AND ep2.dateFinal IS NULL
        )", conn);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                cooks.Add(new EmployeeViewModel
                {
                    Id = reader.GetInt16(0),
                    FullName = reader.GetString(1)
                });
            }
            return cooks;
        }

        private async Task<List<ProductViewModel>> GetAllProducts(NpgsqlConnection conn)
        {
            var products = new List<ProductViewModel>();
            using var cmd = new NpgsqlCommand("SELECT id, nameProduct FROM Products ORDER BY nameProduct", conn);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                products.Add(new ProductViewModel
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1)
                });
            }
            return products;
        }


        public async Task<IActionResult> Finance()
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                var model = new FinanceViewModel();

                // Получаем общую статистику
                using (var cmd = new NpgsqlCommand(@"
            WITH FamilyPaymentsTotal AS (
                SELECT COALESCE(SUM(payment), 0) as total_payments
                FROM PaymentReportFamily
            ),
            InvoicePaymentsTotal AS (
                SELECT COALESCE(SUM(payment), 0) as total_expenses
                FROM PaymentReportInvoice
            ),
            ExpectedPayments AS (
                SELECT COALESCE(SUM(pc.cost), 0) as expected
                FROM PriceChronology pc
                JOIN ChildrensGroup cg ON pc.ID_group = cg.ID
                WHERE pc.dateFinal IS NULL
            )
            SELECT 
                (SELECT total_payments FROM FamilyPaymentsTotal) as total_income,
                (SELECT total_expenses FROM InvoicePaymentsTotal) as total_expenses,
                (SELECT expected FROM ExpectedPayments) as expected_payments", conn))
                {
                    using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        model.TotalIncome = reader.GetDecimal(0);
                        model.TotalExpenses = reader.GetDecimal(1);
                        model.ExpectedPayments = reader.GetDecimal(2);
                        model.TotalBalance = model.TotalIncome - model.TotalExpenses;
                    }
                }

                // Получаем список платежей
                using (var cmd = new NpgsqlCommand(@"
            SELECT 
                prf.ID,
                prf.datePayment,
                f.fullnameResponsible,
                prf.payment,
                e.fullname
            FROM PaymentReportFamily prf
            JOIN Family f ON prf.ID_family = f.ID
            JOIN Employee e ON prf.ID_head = e.ID
            ORDER BY prf.datePayment DESC", conn))
                {
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        model.FamilyPayments.Add(new FamilyPaymentViewModel
                        {
                            Id = reader.GetInt32(0),
                            Date = reader.GetDateTime(1),
                            FamilyName = reader.GetString(2),
                            Amount = reader.GetDecimal(3),
                            HeadTeacherName = reader.GetString(4)
                        });
                    }
                }

                // Получаем список задолженностей
                using (var cmd = new NpgsqlCommand(@"
            WITH LastPayments AS (
                SELECT 
                    ID_family,
                    MAX(datePayment) as last_payment_date
                FROM PaymentReportFamily
                GROUP BY ID_family
            ),
            TotalDebts AS (
                SELECT 
                    f.ID as family_id,
                    f.fullnameResponsible,
                    COALESCE(pc.cost * COUNT(DISTINCT c.ID), 0) - COALESCE(SUM(prf.payment), 0) as debt
                FROM Family f
                JOIN Child c ON f.ID = c.ID_family
                JOIN ChildrensGroup cg ON c.ID_group = cg.ID
                JOIN PriceChronology pc ON cg.ID = pc.ID_group
                LEFT JOIN PaymentReportFamily prf ON f.ID = prf.ID_family
                WHERE pc.dateFinal IS NULL
                GROUP BY f.ID, f.fullnameResponsible, pc.cost
                HAVING COALESCE(pc.cost * COUNT(DISTINCT c.ID), 0) - COALESCE(SUM(prf.payment), 0) > 0
            )
            SELECT 
                td.family_id,
                td.fullnameResponsible,
                td.debt,
                lp.last_payment_date
            FROM TotalDebts td
            LEFT JOIN LastPayments lp ON td.family_id = lp.ID_family
            ORDER BY td.debt DESC", conn))
                {
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        model.Debts.Add(new DebtViewModel
                        {
                            FamilyId = reader.GetInt32(0),
                            FamilyName = reader.GetString(1),
                            Amount = reader.GetDecimal(2),
                            LastPaymentDate = !reader.IsDBNull(3) ? reader.GetDateTime(3) : null
                        });
                    }
                }

                // Получаем список семей для выпадающего списка
                using (var cmd = new NpgsqlCommand(@"
            SELECT ID, fullnameResponsible
            FROM Family
            ORDER BY fullnameResponsible", conn))
                {
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        model.Families.Add(new FamilySelectViewModel
                        {
                            Id = reader.GetInt32(0),
                            Name = reader.GetString(1)
                        });
                    }
                }

                model.ParentPayments = model.FamilyPayments.Sum(p => p.Amount);
                model.TotalDebt = model.Debts.Sum(d => d.Amount);
                model.PurchaseExpenses = model.TotalExpenses;

                // Получаем количество накладных
                using (var cmd = new NpgsqlCommand(@"
            SELECT 
                COUNT(CASE WHEN statusInvoice = 'Ухвалено' THEN 1 END) as approved,
                COUNT(CASE WHEN statusInvoice = 'Перевіряється' THEN 1 END) as pending
            FROM Invoice", conn))
                {
                    using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        model.ApprovedInvoices = reader.GetInt32(0);
                        model.PendingInvoices = reader.GetInt32(1);
                    }
                }

                return View(model);
            }
        }

        [HttpPost]
        public async Task<IActionResult> AddFamilyPayment(int familyId, decimal amount)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using var transaction = await conn.BeginTransactionAsync();
                try
                {
                    // Получаем имя текущего пользователя из сессии
                    var username = HttpContext.Session.GetString("Username");

                    // Получаем ID заведующего по имени из сессии
                    Int16 headTeacherId;
                    using (var userCmd = new NpgsqlCommand(
                        "SELECT ID FROM Employee WHERE fullname = @username", conn))
                    {
                        userCmd.Parameters.AddWithValue("username", username);
                        headTeacherId = (Int16)await userCmd.ExecuteScalarAsync();
                    }

                    // Добавляем запись об оплате
                    using var cmd = new NpgsqlCommand(@"
                INSERT INTO PaymentReportFamily (ID_family, ID_head, payment)
                VALUES (@familyId, @headId, @amount)", conn);

                    cmd.Parameters.AddWithValue("familyId", familyId);
                    cmd.Parameters.AddWithValue("headId", headTeacherId);
                    cmd.Parameters.AddWithValue("amount", amount);

                    await cmd.ExecuteNonQueryAsync();
                    await transaction.CommitAsync();

                    return Json(new { success = true });
                }
                catch
                {
                    await transaction.RollbackAsync();
                    return Json(new { success = false });
                }
            }
        }

        [HttpPost]
        public async Task<IActionResult> UpdateFamilyPayment([FromBody] UpdatePaymentViewModel model)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using var transaction = await conn.BeginTransactionAsync();
                try
                {
                    using var cmd = new NpgsqlCommand(@"
                UPDATE PaymentReportFamily 
                SET payment = @amount
                WHERE ID = @id", conn);

                    cmd.Parameters.AddWithValue("id", model.Id);
                    cmd.Parameters.AddWithValue("amount", model.Amount);

                    await cmd.ExecuteNonQueryAsync();
                    await transaction.CommitAsync();

                    return Json(new { success = true });
                }
                catch
                {
                    await transaction.RollbackAsync();
                    return Json(new { success = false });
                }
            }
        }

        [HttpPost]
        public async Task<IActionResult> DeleteFamilyPayment(int id)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using var transaction = await conn.BeginTransactionAsync();
                try
                {
                    using var cmd = new NpgsqlCommand(@"
                DELETE FROM PaymentReportFamily 
                WHERE ID = @id", conn);

                    cmd.Parameters.AddWithValue("id", id);
                    await cmd.ExecuteNonQueryAsync();
                    await transaction.CommitAsync();

                    return Json(new { success = true });
                }
                catch
                {
                    await transaction.RollbackAsync();
                    return Json(new { success = false });
                }
            }
        }


        public async Task<IActionResult> GetSalaryReport(DateTime startDate, DateTime endDate)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using var cmd = new NpgsqlCommand(@"
            SELECT 
                e.fullname,
                p.postTitle,
                s.salary,
                COALESCE(SUM(bf.amountOfMoney), 0) as bonuses_fines,
                s.salary + COALESCE(SUM(bf.amountOfMoney), 0) as total
            FROM Employee e
            JOIN EmployeeAndPost ep ON e.ID = ep.ID_employee
            JOIN Post p ON ep.ID_post = p.ID
            JOIN Salary s ON p.ID = s.ID_post
            LEFT JOIN BonusAndFine bf ON ep.ID = bf.ID_employeeAndPost
            WHERE (bf.dateAdd IS NULL OR bf.dateAdd BETWEEN @startDate AND @endDate)
            AND s.dateFinal IS NULL
            GROUP BY e.fullname, p.postTitle, s.salary", conn);

                cmd.Parameters.AddWithValue("startDate", startDate);
                cmd.Parameters.AddWithValue("endDate", endDate);

                var report = new List<SalaryReportViewModel>();
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    report.Add(new SalaryReportViewModel
                    {
                        EmployeeName = reader.GetString(0),
                        Position = reader.GetString(1),
                        BaseSalary = reader.GetDecimal(2),
                        BonusesAndFines = reader.GetDecimal(3),
                        TotalSalary = reader.GetDecimal(4)
                    });
                }
                return View(report);
            }
        }

        public async Task<IActionResult> GetInvoiceExpensesReport(DateTime startDate, DateTime endDate)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using var cmd = new NpgsqlCommand(@"
            SELECT 
                i.ID,
                i.dateCreate,
                e.fullname as cook_name,
                SUM(il.actualAmount * il.cost) as total_cost,
                i.statusInvoice
            FROM Invoice i
            JOIN Employee e ON i.ID_cook = e.ID
            JOIN InvoiceList il ON i.ID = il.ID_invoice
            WHERE i.dateCreate BETWEEN @startDate AND @endDate
            GROUP BY i.ID, i.dateCreate, e.fullname, i.statusInvoice
            ORDER BY i.dateCreate DESC", conn);

                cmd.Parameters.AddWithValue("startDate", startDate);
                cmd.Parameters.AddWithValue("endDate", endDate);

                var report = new List<InvoiceExpenseReportViewModel>();
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    report.Add(new InvoiceExpenseReportViewModel
                    {
                        InvoiceId = reader.GetInt32(0),
                        Date = reader.GetDateTime(1),
                        CookName = reader.GetString(2),
                        TotalAmount = reader.GetDecimal(3),
                        Status = reader.GetString(4)
                    });
                }
                return View(report);
            }
        }

        public async Task<IActionResult> GetParentPaymentsReport(DateTime startDate, DateTime endDate)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using var cmd = new NpgsqlCommand(@"
            WITH ExpectedPayments AS (
                SELECT 
                    f.ID as family_id,
                    f.fullnameResponsible,
                    pc.cost * COUNT(DISTINCT c.ID) as expected_amount
                FROM Family f
                JOIN Child c ON f.ID = c.ID_family
                JOIN ChildrensGroup cg ON c.ID_group = cg.ID
                JOIN PriceChronology pc ON cg.ID = pc.ID_group
                WHERE pc.dateFinal IS NULL
                GROUP BY f.ID, f.fullnameResponsible, pc.cost
            ),
            ActualPayments AS (
                SELECT 
                    f.ID as family_id,
                    SUM(prf.payment) as paid_amount
                FROM Family f
                JOIN PaymentReportFamily prf ON f.ID = prf.ID_family
                WHERE prf.datePayment BETWEEN @startDate AND @endDate
                GROUP BY f.ID
            )
            SELECT 
                ep.fullnameResponsible,
                ep.expected_amount,
                COALESCE(ap.paid_amount, 0) as paid_amount,
                ep.expected_amount - COALESCE(ap.paid_amount, 0) as debt
            FROM ExpectedPayments ep
            LEFT JOIN ActualPayments ap ON ep.family_id = ap.family_id
            ORDER BY ep.fullnameResponsible", conn);

                cmd.Parameters.AddWithValue("startDate", startDate);
                cmd.Parameters.AddWithValue("endDate", endDate);

                var report = new List<ParentPaymentReportViewModel>();
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    report.Add(new ParentPaymentReportViewModel
                    {
                        FamilyName = reader.GetString(0),
                        ExpectedAmount = reader.GetDecimal(1),
                        PaidAmount = reader.GetDecimal(2),
                        Debt = reader.GetDecimal(3)
                    });
                }
                return View(report);
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetPaymentDetails(int id)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using var cmd = new NpgsqlCommand(@"
            SELECT 
                prf.datePayment,
                f.fullnameResponsible,
                prf.payment,
                e.fullname as head_name,
                (
                    SELECT STRING_AGG(c.fullname, ', ')
                    FROM Child c
                    WHERE c.ID_family = f.ID
                    AND c.dateFinal IS NULL
                ) as children_names
            FROM PaymentReportFamily prf
            JOIN Family f ON prf.ID_family = f.ID
            JOIN Employee e ON prf.ID_head = e.ID
            WHERE prf.ID = @id", conn);

                cmd.Parameters.AddWithValue("id", id);
                using var reader = await cmd.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    var details = new PaymentDetailsViewModel
                    {
                        Date = reader.GetDateTime(0),
                        FamilyName = reader.GetString(1),
                        Amount = reader.GetDecimal(2),
                        HeadTeacherName = reader.GetString(3),
                        ChildrenNames = !reader.IsDBNull(4) ? reader.GetString(4) : ""
                    };
                    return Json(details);
                }
                return NotFound();
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetDebtDetails(string familyName)
        {
            if (string.IsNullOrEmpty(familyName))
            {
                return BadRequest("Family name is required");
            }

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                // Сначала найдем ID семьи по имени
                int familyId;
                using (var cmdFindFamily = new NpgsqlCommand(
                    "SELECT ID FROM Family WHERE fullnameResponsible = @familyName", conn))
                {
                    cmdFindFamily.Parameters.AddWithValue("familyName", familyName);
                    var result = await cmdFindFamily.ExecuteScalarAsync();
                    if (result == null)
                    {
                        return BadRequest($"Family not found: {familyName}");
                    }
                    familyId = (int)result;
                }

                var details = new DebtDetailsViewModel
                {
                    Children = new List<ChildDebtInfo>(),
                    RecentPayments = new List<PaymentInfo>()
                };

                // Остальной код остается тем же, просто используем найденный familyId
                using (var cmdChildren = new NpgsqlCommand(@"
            SELECT 
                c.fullname,
                cg.nameGroup,
                pc.cost
            FROM Child c
            JOIN ChildrensGroup cg ON c.ID_group = cg.ID
            JOIN PriceChronology pc ON cg.ID = pc.ID_group
            WHERE c.ID_family = @familyId
            AND c.dateFinal IS NULL
            AND pc.dateFinal IS NULL", conn))
                {
                    cmdChildren.Parameters.AddWithValue("familyId", familyId);
                    using var reader = await cmdChildren.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        details.Children.Add(new ChildDebtInfo
                        {
                            Name = reader.GetString(0),
                            GroupName = reader.GetString(1),
                            MonthlyFee = reader.GetDecimal(2)
                        });
                    }
                }

                // Платежи
                using (var cmdPayments = new NpgsqlCommand(@"
            SELECT 
                datePayment,
                payment
            FROM PaymentReportFamily
            WHERE ID_family = @familyId
            ORDER BY datePayment DESC
            LIMIT 5", conn))
                {
                    cmdPayments.Parameters.AddWithValue("familyId", familyId);
                    using var reader = await cmdPayments.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        details.RecentPayments.Add(new PaymentInfo
                        {
                            Date = reader.GetDateTime(0),
                            Amount = reader.GetDecimal(1)
                        });
                    }
                }

                // Общий долг
                using (var cmdDebt = new NpgsqlCommand(@"
            WITH ExpectedPayment AS (
                SELECT COALESCE(SUM(pc.cost), 0) as total_expected
                FROM Child c
                JOIN ChildrensGroup cg ON c.ID_group = cg.ID
                JOIN PriceChronology pc ON cg.ID = pc.ID_group
                WHERE c.ID_family = @familyId
                AND c.dateFinal IS NULL
                AND pc.dateFinal IS NULL
            ),
            ActualPayment AS (
                SELECT COALESCE(SUM(payment), 0) as total_paid
                FROM PaymentReportFamily
                WHERE ID_family = @familyId
            )
            SELECT 
                ep.total_expected - ap.total_paid as total_debt
            FROM ExpectedPayment ep
            CROSS JOIN ActualPayment ap", conn))
                {
                    cmdDebt.Parameters.AddWithValue("familyId", familyId);
                    details.TotalDebt = (decimal)await cmdDebt.ExecuteScalarAsync();
                }

                return Json(details);
            }
        }

        public async Task<IActionResult> FinanceSalaryReport(DateTime? startDate = null, DateTime? endDate = null)
        {
            if (!startDate.HasValue) startDate = DateTime.Today.AddMonths(-1);
            if (!endDate.HasValue) endDate = DateTime.Today;

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                // Основной запрос для получения данных
                using var cmd = new NpgsqlCommand(@"
            WITH EmployeeStats AS (
                SELECT 
                    e.fullname,
                    p.postTitle,
                    s.salary as base_salary,
                    COALESCE(SUM(CASE WHEN bf.amountOfMoney > 0 THEN bf.amountOfMoney ELSE 0 END), 0) as total_bonuses,
                    COALESCE(SUM(CASE WHEN bf.amountOfMoney < 0 THEN ABS(bf.amountOfMoney) ELSE 0 END), 0) as total_fines
                FROM Employee e
                JOIN EmployeeAndPost ep ON e.id = ep.id_employee
                JOIN Post p ON ep.id_post = p.id
                JOIN Salary s ON p.id = s.id_post
                LEFT JOIN BonusAndFine bf ON ep.id = bf.id_employeeAndPost 
                    AND bf.dateAdd BETWEEN @startDate AND @endDate
                WHERE ep.dateFinal IS NULL 
                    AND s.dateFinal IS NULL
                GROUP BY e.fullname, p.postTitle, s.salary
            )
            SELECT * FROM EmployeeStats
            ORDER BY base_salary DESC", conn);

                cmd.Parameters.AddWithValue("startDate", startDate);
                cmd.Parameters.AddWithValue("endDate", endDate);

                var salaryData = new List<SalaryReportViewModel>();
                var totalBaseSalary = 0m;
                var totalBonuses = 0m;
                var totalFines = 0m;

                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var baseSalary = reader.GetDecimal(2);
                        var bonuses = reader.GetDecimal(3);
                        var fines = reader.GetDecimal(4);

                        totalBaseSalary += baseSalary;
                        totalBonuses += bonuses;
                        totalFines += fines;

                        salaryData.Add(new SalaryReportViewModel
                        {
                            EmployeeName = reader.GetString(0),
                            Position = reader.GetString(1),
                            BaseSalary = baseSalary,
                            BonusesAndFines = bonuses - fines,
                            TotalSalary = baseSalary + bonuses - fines
                        });
                    }
                }

                var totalSalaryFund = totalBaseSalary + totalBonuses - totalFines;
                var bonusPercent = totalSalaryFund > 0 ? (totalBonuses * 100M / totalSalaryFund) : 0;
                var finesPercent = totalSalaryFund > 0 ? (totalFines * 100M / totalSalaryFund) : 0;
                var averageSalary = salaryData.Any() ? salaryData.Average(x => x.TotalSalary) : 0;

                // Группировка по должностям
                var positionStats = salaryData
                    .GroupBy(x => x.Position)
                    .Select(g => new
                    {
                        position = g.Key,
                        total = g.Sum(x => x.TotalSalary)
                    })
                    .ToList();

                // Установка всех необходимых ViewBag значений
                ViewData["StartDate"] = startDate?.ToString("yyyy-MM-dd");
                ViewData["EndDate"] = endDate?.ToString("yyyy-MM-dd");
                ViewData["TotalSalaryFund"] = totalSalaryFund;
                ViewData["TotalBonuses"] = totalBonuses;
                ViewData["TotalFines"] = totalFines;
                ViewData["BonusPercent"] = bonusPercent;
                ViewData["FinesPercent"] = finesPercent;
                ViewData["AverageSalary"] = averageSalary;
                ViewData["PositionData"] = JsonSerializer.Serialize(positionStats);

                return View(salaryData);
            }
        }

        private async Task<List<SalaryReportViewModel>> GetSalaryData(
            NpgsqlConnection conn,
            DateTime startDate,
            DateTime endDate)
        {
            using var cmd = new NpgsqlCommand(@"
                WITH CurrentPositions AS (
                    SELECT 
                        e.ID,
                        e.fullname,
                        p.postTitle,
                        s.salary as base_salary,
                        ep.ID as employee_post_id,
                        ep.dateStart,
                        ep.dateFinal
                    FROM Employee e
                    JOIN EmployeeAndPost ep ON e.ID = ep.ID_employee
                    JOIN Post p ON ep.ID_post = p.ID
                    JOIN Salary s ON p.ID = s.ID_post
                    WHERE (ep.dateFinal IS NULL OR ep.dateFinal >= @startDate)
                        AND ep.dateStart <= @endDate
                        AND (s.dateFinal IS NULL OR s.dateFinal >= @startDate)
                        AND s.dateStart <= @endDate
                )
                SELECT 
                    cp.fullname,
                    cp.postTitle,
                    cp.base_salary,
                    COALESCE(SUM(CASE 
                        WHEN bf.amountOfMoney > 0 THEN bf.amountOfMoney 
                        ELSE 0 
                    END), 0) as bonuses,
                    COALESCE(SUM(CASE 
                        WHEN bf.amountOfMoney < 0 THEN bf.amountOfMoney 
                        ELSE 0 
                    END), 0) as fines
                FROM CurrentPositions cp
                LEFT JOIN BonusAndFine bf ON cp.employee_post_id = bf.ID_employeeAndPost
                    AND bf.dateAdd BETWEEN @startDate AND @endDate
                GROUP BY 
                    cp.fullname,
                    cp.postTitle,
                    cp.base_salary", conn);

            cmd.Parameters.AddWithValue("startDate", startDate);
            cmd.Parameters.AddWithValue("endDate", endDate);

            var result = new List<SalaryReportViewModel>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new SalaryReportViewModel
                {
                    EmployeeName = reader.GetString(0),
                    Position = reader.GetString(1),
                    BaseSalary = reader.GetDecimal(2),
                    BonusesAndFines = reader.GetDecimal(3) + reader.GetDecimal(4),
                    TotalSalary = reader.GetDecimal(2) + reader.GetDecimal(3) + reader.GetDecimal(4)
                });
            }

            return result;
        }

        private async Task<List<(DateTime Date, decimal Amount)>> GetIncomeData(
            NpgsqlConnection conn,
            DateTime startDate,
            DateTime endDate)
        {
            using var cmd = new NpgsqlCommand(@"
                SELECT 
                    datePayment,
                    payment
                FROM PaymentReportFamily 
                WHERE datePayment BETWEEN @startDate AND @endDate", conn);

            cmd.Parameters.AddWithValue("startDate", startDate);
            cmd.Parameters.AddWithValue("endDate", endDate);

            var result = new List<(DateTime Date, decimal Amount)>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add((
                    reader.GetDateTime(0),
                    reader.GetDecimal(1)
                ));
            }

            return result;
        }

        private async Task<List<(DateTime Date, decimal Amount, string Description)>> GetBonusesData(
            NpgsqlConnection conn,
            DateTime startDate,
            DateTime endDate)
        {
            using var cmd = new NpgsqlCommand(@"
                SELECT 
                    bf.dateAdd,
                    bf.amountOfMoney,
                    bf.description
                FROM BonusAndFine bf
                WHERE bf.dateAdd BETWEEN @startDate AND @endDate", conn);

            cmd.Parameters.AddWithValue("startDate", startDate);
            cmd.Parameters.AddWithValue("endDate", endDate);

            var result = new List<(DateTime Date, decimal Amount, string Description)>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add((
                    reader.GetDateTime(0),
                    reader.GetDecimal(1),
                    reader.GetString(2)
                ));
            }

            return result;
        }

        private object GroupDataByPeriod(
            List<SalaryReportViewModel> salaryData,
            List<(DateTime Date, decimal Amount, string Description)> bonusesData,
            string period,
            DateTime startDate,
            DateTime endDate)
        {
            // Определяем функцию группировки в зависимости от периода
            Func<DateTime, DateTime> truncateDate = period switch
            {
                "month" => d => new DateTime(d.Year, d.Month, 1),
                "quarter" => d => new DateTime(d.Year, ((d.Month - 1) / 3) * 3 + 1, 1),
                "year" => d => new DateTime(d.Year, 1, 1),
                _ => d => new DateTime(d.Year, d.Month, 1)
            };

            // Группируем бонусы по периодам
            var groupedBonuses = bonusesData
                .GroupBy(x => truncateDate(x.Date))
                .ToDictionary(
                    g => g.Key,
                    g => new
                    {
                        TotalBonuses = g.Where(x => x.Amount > 0).Sum(x => x.Amount),
                        TotalFines = Math.Abs(g.Where(x => x.Amount < 0).Sum(x => x.Amount))
                    }
                );

            // Создаем последовательность дат для периодов
            var dates = new List<DateTime>();
            var current = truncateDate(startDate);
            var end = truncateDate(endDate);

            while (current <= end)
            {
                dates.Add(current);
                current = period switch
                {
                    "month" => current.AddMonths(1),
                    "quarter" => current.AddMonths(3),
                    "year" => current.AddYears(1),
                    _ => current.AddMonths(1)
                };
            }

            // Формируем итоговые данные
            return dates.Select(date =>
            {
                var bonusData = groupedBonuses.GetValueOrDefault(date, new { TotalBonuses = 0m, TotalFines = 0m });

                return new
                {
                    period = period switch
                    {
                        "month" => date.ToString("MMMM yyyy", new CultureInfo("uk-UA")),
                        "quarter" => $"{date.ToString("yyyy", new CultureInfo("uk-UA"))} Q{((date.Month - 1) / 3) + 1}",
                        "year" => date.ToString("yyyy", new CultureInfo("uk-UA")),
                        _ => date.ToString("MMMM yyyy", new CultureInfo("uk-UA"))
                    },
                    baseSalary = salaryData.Sum(x => x.BaseSalary),
                    bonuses = bonusData.TotalBonuses,
                    fines = bonusData.TotalFines,
                    total = salaryData.Sum(x => x.BaseSalary) + bonusData.TotalBonuses - bonusData.TotalFines
                };
            }).ToList();
        }

        private Dictionary<string, string> CalculateEmployeeChanges(
            List<SalaryReportViewModel> currentData,
            List<SalaryReportViewModel> previousData)
        {
            var changes = new Dictionary<string, string>();

            foreach (var employee in currentData)
            {
                var previous = previousData.FirstOrDefault(x => x.EmployeeName == employee.EmployeeName);
                if (previous != null && previous.TotalSalary > 0)
                {
                    var change = (employee.TotalSalary - previous.TotalSalary) / previous.TotalSalary * 100;
                    changes[employee.EmployeeName] = change.ToString("F1");
                }
                else
                {
                    changes[employee.EmployeeName] = "0.0";
                }
            }

            return changes;
        }

        public async Task<IActionResult> FinanceInvoiceExpensesReport(DateTime? startDate = null, DateTime? endDate = null)
        {
            if (!startDate.HasValue) startDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            if (!endDate.HasValue) endDate = DateTime.Now;

            var report = new List<InvoiceExpenseReportViewModel>();
            var chartData = new List<object>();
            var statusData = new List<object>();

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                // Получаем основные данные о накладных
                using (var cmd = new NpgsqlCommand(@"
            SELECT 
                i.id,
                i.dateCreate,
                e.fullname as cook_name,
                i.statusInvoice,
                COALESCE(SUM(il.actualAmount * il.cost), 0) as total_cost,
                STRING_AGG(
                    CONCAT(
                        p.nameProduct, 
                        ' (', 
                        CASE 
                            WHEN il.actualAmount IS NOT NULL THEN il.actualAmount::text 
                            ELSE il.amount::text 
                        END,
                        ' ', 
                        il.measure,
                        CASE 
                            WHEN il.cost IS NOT NULL THEN ', ' || il.cost::text || ' грн' 
                            ELSE ''
                        END,
                        ')'
                    ),
                    ', '
                ) as products_list
            FROM Invoice i
            JOIN Employee e ON i.ID_cook = e.ID
            LEFT JOIN InvoiceList il ON i.id = il.ID_invoice
            LEFT JOIN Products p ON il.ID_products = p.ID
            WHERE i.dateCreate BETWEEN @startDate AND @endDate
            GROUP BY i.id, i.dateCreate, e.fullname, i.statusInvoice
            ORDER BY i.dateCreate DESC", conn))
                {
                    cmd.Parameters.AddWithValue("startDate", startDate);
                    cmd.Parameters.AddWithValue("endDate", endDate);

                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        report.Add(new InvoiceExpenseReportViewModel
                        {
                            InvoiceId = reader.GetInt32(0),
                            Date = reader.GetDateTime(1),
                            CookName = reader.GetString(2),
                            Status = reader.GetString(3),
                            TotalAmount = reader.GetDecimal(4),
                            ProductsList = !reader.IsDBNull(5) ? reader.GetString(5) : ""
                        });
                    }
                }

                // Получаем данные для графика
                using (var chartCmd = new NpgsqlCommand(@"
            SELECT 
                DATE_TRUNC('day', i.dateCreate)::date as date,
                COALESCE(SUM(il.actualAmount * il.cost), 0) as daily_total
            FROM Invoice i
            LEFT JOIN InvoiceList il ON i.ID = il.ID_invoice
            WHERE i.dateCreate BETWEEN @startDate AND @endDate
            GROUP BY DATE_TRUNC('day', i.dateCreate)::date
            ORDER BY date", conn))
                {
                    chartCmd.Parameters.AddWithValue("startDate", startDate);
                    chartCmd.Parameters.AddWithValue("endDate", endDate);

                    using var chartReader = await chartCmd.ExecuteReaderAsync();
                    while (await chartReader.ReadAsync())
                    {
                        chartData.Add(new
                        {
                            date = ((DateTime)chartReader.GetDateTime(0)).ToString("dd.MM.yyyy"),
                            amount = chartReader.GetDecimal(1)
                        });
                    }
                }

                // Получаем статистику по статусам
                using (var statusCmd = new NpgsqlCommand(@"
            SELECT 
                i.statusInvoice,
                COUNT(*) as count,
                COALESCE(SUM(il.actualAmount * il.cost), 0) as status_total
            FROM Invoice i
            LEFT JOIN InvoiceList il ON i.ID = il.ID_invoice
            WHERE i.dateCreate BETWEEN @startDate AND @endDate
            GROUP BY i.statusInvoice", conn))
                {
                    statusCmd.Parameters.AddWithValue("startDate", startDate);
                    statusCmd.Parameters.AddWithValue("endDate", endDate);

                    using var statusReader = await statusCmd.ExecuteReaderAsync();
                    while (await statusReader.ReadAsync())
                    {
                        statusData.Add(new
                        {
                            status = statusReader.GetString(0),
                            count = statusReader.GetInt64(1),
                            total = statusReader.GetDecimal(2)
                        });
                    }
                }
            }

            ViewBag.ChartData = JsonSerializer.Serialize(chartData);
            ViewBag.StatusData = JsonSerializer.Serialize(statusData);
            ViewBag.StartDate = startDate?.ToString("yyyy-MM-dd");
            ViewBag.EndDate = endDate?.ToString("yyyy-MM-dd");

            return View(report);
        }

        public async Task<IActionResult> FinanceParentPaymentsReport(DateTime? startDate = null, DateTime? endDate = null)
        {
            if (!startDate.HasValue) startDate = DateTime.Today.AddMonths(-1);
            if (!endDate.HasValue) endDate = DateTime.Today;

            var report = new List<ParentPaymentReportViewModel>();
            var chartData = new List<object>();

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                // Основной запрос для получения данных по платежам
                using (var cmd = new NpgsqlCommand(@"
            WITH CurrentPrices AS (
                SELECT 
                    c.ID_family,
                    SUM(pc.cost) as monthly_cost
                FROM Child c
                JOIN ChildrensGroup cg ON c.ID_group = cg.ID
                JOIN PriceChronology pc ON cg.ID = pc.ID_group
                WHERE c.dateFinal IS NULL 
                AND pc.dateFinal IS NULL
                GROUP BY c.ID_family
            ),
            PaymentsInPeriod AS (
                SELECT 
                    f.ID as family_id,
                    f.fullnameResponsible as family_name,
                    COALESCE(cp.monthly_cost, 0) as expected_amount,
                    COALESCE(SUM(prf.payment), 0) as paid_amount
                FROM Family f
                LEFT JOIN CurrentPrices cp ON f.ID = cp.ID_family
                LEFT JOIN PaymentReportFamily prf ON f.ID = prf.ID_family 
                    AND prf.datePayment BETWEEN @startDate AND @endDate
                GROUP BY f.ID, f.fullnameResponsible, cp.monthly_cost
                HAVING COALESCE(cp.monthly_cost, 0) > 0
            )
            SELECT 
                family_name,
                expected_amount,
                paid_amount,
                expected_amount - paid_amount as debt
            FROM PaymentsInPeriod
            ORDER BY debt DESC", conn))
                {
                    cmd.Parameters.AddWithValue("startDate", startDate);
                    cmd.Parameters.AddWithValue("endDate", endDate);

                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        report.Add(new ParentPaymentReportViewModel
                        {
                            FamilyName = reader.GetString(0),
                            ExpectedAmount = reader.GetDecimal(1),
                            PaidAmount = reader.GetDecimal(2),
                            Debt = reader.GetDecimal(3)
                        });
                    }
                }

                // Получаем данные для графика ежедневных платежей
                using (var chartCmd = new NpgsqlCommand(@"
            SELECT 
                DATE_TRUNC('day', prf.datePayment)::date as payment_date,
                SUM(prf.payment) as daily_total
            FROM PaymentReportFamily prf
            WHERE prf.datePayment BETWEEN @startDate AND @endDate
            GROUP BY DATE_TRUNC('day', prf.datePayment)::date
            ORDER BY payment_date", conn))
                {
                    chartCmd.Parameters.AddWithValue("startDate", startDate);
                    chartCmd.Parameters.AddWithValue("endDate", endDate);

                    using var chartReader = await chartCmd.ExecuteReaderAsync();
                    while (await chartReader.ReadAsync())
                    {
                        chartData.Add(new
                        {
                            date = ((DateTime)chartReader.GetDateTime(0)).ToString("dd.MM.yyyy"),
                            amount = chartReader.GetDecimal(1)
                        });
                    }
                }

                // Дополнительная статистика: процент оплат по группам
                using (var groupStatsCmd = new NpgsqlCommand(@"
            WITH GroupPayments AS (
                SELECT 
                    cg.nameGroup,
                    SUM(pc.cost) as expected_amount,
                    COALESCE(SUM(prf.payment), 0) as paid_amount
                FROM ChildrensGroup cg
                JOIN Child c ON cg.ID = c.ID_group
                JOIN PriceChronology pc ON cg.ID = pc.ID_group
                LEFT JOIN PaymentReportFamily prf ON c.ID_family = prf.ID_family 
                    AND prf.datePayment BETWEEN @startDate AND @endDate
                WHERE c.dateFinal IS NULL 
                AND pc.dateFinal IS NULL
                GROUP BY cg.nameGroup
            )
            SELECT 
                nameGroup,
                expected_amount,
                paid_amount,
                CASE 
                    WHEN expected_amount > 0 
                    THEN ROUND((paid_amount * 100.0 / expected_amount)::numeric, 1)
                    ELSE 0 
                END as payment_percentage
            FROM GroupPayments
            ORDER BY payment_percentage DESC", conn))
                {
                    groupStatsCmd.Parameters.AddWithValue("startDate", startDate);
                    groupStatsCmd.Parameters.AddWithValue("endDate", endDate);

                    var groupStats = new List<object>();
                    using var statsReader = await groupStatsCmd.ExecuteReaderAsync();
                    while (await statsReader.ReadAsync())
                    {
                        groupStats.Add(new
                        {
                            group = statsReader.GetString(0),
                            expected = statsReader.GetDecimal(1),
                            paid = statsReader.GetDecimal(2),
                            percentage = statsReader.GetDecimal(3)
                        });
                    }
                    ViewBag.GroupStats = JsonSerializer.Serialize(groupStats);
                }
            }

            ViewBag.ChartData = JsonSerializer.Serialize(chartData);
            ViewBag.StartDate = startDate?.ToString("yyyy-MM-dd");
            ViewBag.EndDate = endDate?.ToString("yyyy-MM-dd");
            ViewBag.TotalExpected = report.Sum(x => x.ExpectedAmount);
            ViewBag.TotalPaid = report.Sum(x => x.PaidAmount);
            ViewBag.TotalDebt = report.Sum(x => x.Debt);
            ViewBag.PaymentPercentage = ViewBag.TotalExpected > 0
                ? (ViewBag.TotalPaid * 100.0m / ViewBag.TotalExpected)
                : 0;

            return View(report);
        }

        public async Task<IActionResult> Groups()
        {
            var groups = new List<GroupViewModel>();

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                using (var cmd = new NpgsqlCommand(@"
            WITH GroupAttendance AS (
                SELECT 
                    c.ID_group,
                    COUNT(t.ID) as total_records,
                    COUNT(CASE WHEN t.statusVisit = 'Присутній' THEN 1 END) as present_count
                FROM Child c
                LEFT JOIN Tabulation t ON c.ID = t.ID_child
                WHERE c.dateFinal IS NULL
                GROUP BY c.ID_group
            )
            SELECT 
                cg.ID,
                cg.nameGroup,
                e.fullname as teacher_name,
                e.ID as teacher_id,
                cg.capacity,
                COUNT(c.ID) as current_count,
                pc.cost as current_price,
                COALESCE(ga.present_count * 100.0 / NULLIF(ga.total_records, 0), 0) as attendance_percentage
            FROM ChildrensGroup cg
            JOIN Employee e ON cg.ID_teacher = e.ID
            LEFT JOIN Child c ON cg.ID = c.ID_group AND c.dateFinal IS NULL
            LEFT JOIN PriceChronology pc ON cg.ID = pc.ID_group AND pc.dateFinal IS NULL
            LEFT JOIN GroupAttendance ga ON cg.ID = ga.ID_group
            GROUP BY cg.ID, cg.nameGroup, e.fullname, e.ID, cg.capacity, pc.cost, 
                     ga.present_count, ga.total_records
            ORDER BY cg.nameGroup", conn))
                {
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        groups.Add(new GroupViewModel
                        {
                            Id = reader.GetInt32(0),
                            Name = reader.GetString(1),
                            TeacherName = reader.GetString(2),
                            TeacherId = reader.GetInt32(3),
                            Capacity = reader.GetInt32(4),
                            CurrentCount = reader.GetInt32(5),
                            CurrentPrice = !reader.IsDBNull(6) ? reader.GetDecimal(6) : 0,
                            AttendancePercentage = !reader.IsDBNull(7) ? reader.GetDecimal(7) : 0,
                            Children = new List<ChildInGroupViewModel>()
                        });
                    }
                }

                // Получаем список всех доступных воспитателей для выпадающего списка
                using (var cmd = new NpgsqlCommand(@"
            SELECT DISTINCT e.ID, e.fullname
            FROM Employee e
            JOIN EmployeeAndPost ep ON e.ID = ep.ID_employee
            JOIN Post p ON ep.ID_post = p.ID
            WHERE p.postTitle = 'Вихователь'
            AND ep.dateFinal IS NULL
            ORDER BY e.fullname", conn))
                {
                    var teachers = new List<SelectListItem>();
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        teachers.Add(new SelectListItem
                        {
                            Value = reader.GetInt32(0).ToString(),
                            Text = reader.GetString(1)
                        });
                    }
                    ViewBag.Teachers = teachers;
                }
            }

            return View(groups);
        }

        public async Task<IActionResult> GroupDetails(int id)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                // Получаем информацию о группе
                GroupViewModel group = null;
                using (var cmd = new NpgsqlCommand(@"
            SELECT 
                cg.ID,
                cg.nameGroup,
                e.fullname as teacher_name,
                e.ID as teacher_id,
                cg.capacity,
                pc.cost as current_price
            FROM ChildrensGroup cg
            JOIN Employee e ON cg.ID_teacher = e.ID
            LEFT JOIN PriceChronology pc ON cg.ID = pc.ID_group
            WHERE cg.ID = @groupId AND pc.dateFinal IS NULL", conn))
                {
                    cmd.Parameters.AddWithValue("groupId", id);

                    using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        group = new GroupViewModel
                        {
                            Id = reader.GetInt32(0),
                            Name = reader.GetString(1),
                            TeacherName = reader.GetString(2),
                            TeacherId = reader.GetInt32(3),
                            Capacity = reader.GetInt32(4),
                            CurrentPrice = !reader.IsDBNull(5) ? reader.GetDecimal(5) : 0,
                            Children = new List<ChildInGroupViewModel>()
                        };
                    }
                }

                if (group == null)
                    return NotFound();

                // Получаем список детей в группе
                using (var cmd = new NpgsqlCommand(@"
            WITH ChildAttendance AS (
                SELECT 
                    ID_child,
                    COUNT(*) as total_records,
                    COUNT(CASE WHEN statusVisit = 'Присутній' THEN 1 END) as present_count
                FROM Tabulation
                GROUP BY ID_child
            )
            SELECT 
                c.ID,
                c.fullname,
                f.fullnameResponsible as family_name,
                c.dateStart,
                c.dateFinal,
                STRING_AGG(DISTINCT con.name, ', ') as contraindications,
                t.statusVisit as current_status,
                COALESCE(ca.present_count * 100.0 / NULLIF(ca.total_records, 0), 0) as attendance_percentage
            FROM Child c
            JOIN Family f ON c.ID_family = f.ID
            LEFT JOIN ContraindChildren cc ON c.ID = cc.ID_child
            LEFT JOIN Contraindications con ON cc.ID_contraind = con.ID
            LEFT JOIN Tabulation t ON c.ID = t.ID_child AND t.dateVisit = CURRENT_DATE
            LEFT JOIN ChildAttendance ca ON c.ID = ca.ID_child
            WHERE c.ID_group = @groupId
            GROUP BY c.ID, c.fullname, f.fullnameResponsible, c.dateStart, c.dateFinal, 
                     t.statusVisit, ca.present_count, ca.total_records", conn))
                {
                    cmd.Parameters.AddWithValue("groupId", id);

                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        group.Children.Add(new ChildInGroupViewModel
                        {
                            Id = reader.GetInt32(0),
                            FullName = reader.GetString(1),
                            FamilyName = reader.GetString(2),
                            StartDate = reader.GetDateTime(3),
                            EndDate = !reader.IsDBNull(4) ? reader.GetDateTime(4) : null,
                            Contraindications = !reader.IsDBNull(5)
                                ? reader.GetString(5).Split(", ").ToList()
                                : new List<string>(),
                            AttendanceStatus = !reader.IsDBNull(6) ? reader.GetString(6) : "Відсутній",
                            AttendancePercentage = !reader.IsDBNull(7) ? reader.GetDecimal(7) : 0
                        });
                    }
                }

                group.CurrentCount = group.Children.Count;
                return View(group);
            }
        }

        [HttpPost]
        public async Task<IActionResult> UpdateGroupTeacher(int groupId, int teacherId)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using var cmd = new NpgsqlCommand(@"
            UPDATE ChildrensGroup 
            SET ID_teacher = @teacherId 
            WHERE ID = @groupId", conn);

                cmd.Parameters.AddWithValue("groupId", groupId);
                cmd.Parameters.AddWithValue("teacherId", teacherId);

                await cmd.ExecuteNonQueryAsync();
                return Json(new { success = true });
            }
        }

        [HttpPost]
        public async Task<IActionResult> UpdateGroupPrice(int groupId, decimal price)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using var transaction = await conn.BeginTransactionAsync();

                try
                {
                    // Закрываем текущую цену
                    using (var cmd = new NpgsqlCommand(@"
                UPDATE PriceChronology 
                SET dateFinal = CURRENT_DATE
                WHERE ID_group = @groupId AND dateFinal IS NULL", conn))
                    {
                        cmd.Parameters.AddWithValue("groupId", groupId);
                        await cmd.ExecuteNonQueryAsync();
                    }

                    // Добавляем новую цену
                    using (var cmd = new NpgsqlCommand(@"
                INSERT INTO PriceChronology (ID_group, cost, dateStart)
                VALUES (@groupId, @price, CURRENT_DATE)", conn))
                    {
                        cmd.Parameters.AddWithValue("groupId", groupId);
                        cmd.Parameters.AddWithValue("price", price);
                        await cmd.ExecuteNonQueryAsync();
                    }

                    await transaction.CommitAsync();
                    return Json(new { success = true });
                }
                catch
                {
                    await transaction.RollbackAsync();
                    return Json(new { success = false });
                }
            }
        }


        public async Task<IActionResult> Quarantine()
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                var model = new QuarantineViewModel
                {
                    Groups = new List<GroupQuarantineInfo>(),
                    AttendanceStats = new List<DailyAttendanceStat>()
                };

                // Получаем информацию о посещаемости по группам
                // В методе Quarantine() меняем подсчет посещаемости
                using (var cmd = new NpgsqlCommand(@"
                    WITH CurrentAttendance AS (
                        SELECT 
                            c.ID_group,
                            COUNT(DISTINCT c.ID) as total_children,
                            COUNT(DISTINCT CASE 
                                WHEN t.statusVisit = 'Присутній' THEN c.ID 
                                WHEN t.statusVisit = 'На лікарняному' THEN c.ID 
                            END) as absent_children,
                            COUNT(DISTINCT CASE 
                                WHEN t.statusVisit = 'На лікарняному' THEN c.ID 
                            END) as sick_children
                        FROM Child c
                        LEFT JOIN Tabulation t ON c.ID = t.ID_child
                            AND t.dateVisit = CURRENT_DATE
                        WHERE c.dateFinal IS NULL
                        GROUP BY c.ID_group
                    )
                    SELECT 
                        cg.ID,
                        cg.nameGroup,
                        e.fullname as teacher_name,
                        ca.total_children,
                        ca.absent_children,
                        ca.sick_children,
                        CASE 
                            WHEN ca.total_children > 0 
                            THEN ((ca.total_children - ca.absent_children) * 100.0 / ca.total_children)
                            ELSE 0 
                        END as attendance_percentage
                    FROM ChildrensGroup cg
                    JOIN Employee e ON cg.ID_teacher = e.ID
                    JOIN CurrentAttendance ca ON cg.ID = ca.ID_group
                    ORDER BY attendance_percentage ASC", conn))
                {
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var groupInfo = new GroupQuarantineInfo
                        {
                            GroupId = reader.GetInt32(0),
                            GroupName = reader.GetString(1),
                            TeacherName = reader.GetString(2),
                            TotalChildren = reader.GetInt32(3),
                            AbsentChildren = reader.GetInt32(4),
                            SickChildren = reader.GetInt32(5), // Добавляем больных детей
                            AttendancePercentage = reader.GetDecimal(6),
                            AbsentChildrenDetails = new List<AbsentChildInfo>()
                        };
                        model.Groups.Add(groupInfo);
                    }
                }

                // Получаем детали об отсутствующих детях для каждой группы
                foreach (var group in model.Groups)
                {
                    using (var cmd = new NpgsqlCommand(@"
                WITH ConsecutiveAbsences AS (
                    SELECT 
                        c.ID,
                        c.fullname,
                        t.statusVisit,
                        COUNT(*) FILTER (WHERE t.statusVisit = 'Відсутній') 
                        OVER (PARTITION BY c.ID ORDER BY t.dateVisit DESC) as consecutive_absences
                    FROM Child c
                    JOIN Tabulation t ON c.ID = t.ID_child
                    WHERE c.ID_group = @groupId
                    AND t.dateVisit <= CURRENT_DATE
                    ORDER BY t.dateVisit DESC
                )
                SELECT DISTINCT ON (ID)
                    fullname,
                    statusVisit,
                    consecutive_absences
                FROM ConsecutiveAbsences
                WHERE statusVisit = 'Відсутній'", conn))
                    {
                        cmd.Parameters.AddWithValue("groupId", group.GroupId);

                        using var reader = await cmd.ExecuteReaderAsync();
                        while (await reader.ReadAsync())
                        {
                            group.AbsentChildrenDetails.Add(new AbsentChildInfo
                            {
                                ChildName = reader.GetString(0),
                                Status = reader.GetString(1),
                                ConsecutiveAbsentDays = reader.GetInt32(2)
                            });
                        }
                    }
                }

                // Получаем статистику посещаемости за последние 7 дней
                using (var cmd = new NpgsqlCommand(@"
            WITH DailyStats AS (
                SELECT 
                    t.dateVisit,
                    COUNT(DISTINCT c.ID) as total_children,
                    COUNT(DISTINCT CASE WHEN t.statusVisit = 'Присутній' THEN c.ID END) as present_children
                FROM Child c
                LEFT JOIN Tabulation t ON c.ID = t.ID_child
                WHERE t.dateVisit >= CURRENT_DATE - INTERVAL '7 days'
                AND c.dateFinal IS NULL
                GROUP BY t.dateVisit
            )
            SELECT 
                dateVisit,
                total_children,
                present_children,
                CASE 
                    WHEN total_children > 0 
                    THEN (present_children * 100.0 / total_children)
                    ELSE 0 
                END as attendance_percentage
            FROM DailyStats
            ORDER BY dateVisit", conn))
                {
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        model.AttendanceStats.Add(new DailyAttendanceStat
                        {
                            Date = reader.GetDateTime(0),
                            TotalChildren = reader.GetInt32(1),
                            PresentChildren = reader.GetInt32(2),
                            AttendancePercentage = reader.GetDecimal(3)
                        });
                    }
                }

                // Расчет общей статистики
                var totalChildren = model.Groups.Sum(g => g.TotalChildren);
                model.TotalAbsentChildren = model.Groups.Sum(g => g.AbsentChildren);
                model.OverallAttendancePercentage = totalChildren > 0
                    ? ((totalChildren - model.TotalAbsentChildren) * 100.0M / totalChildren)
                    : 0;

                return View(model);
            }
        }


        [HttpGet]
        public async Task<IActionResult> Groups(int? groupId = null, string period = "day", DateTime? startDate = null, DateTime? endDate = null)
        {
            var model = new HeadTeacherGroupViewModel
            {
                SelectedGroupId = groupId,
                Period = period,
                StartDate = startDate ?? DateTime.Now.AddMonths(-1),
                EndDate = endDate ?? DateTime.Now
            };

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                // Получаем список групп с основной информацией
                using (var cmd = new NpgsqlCommand(@"
            WITH GroupAttendance AS (
                SELECT 
                    c.ID_group,
                    COUNT(t.ID) as total_records,
                    COUNT(CASE WHEN t.statusVisit = 'Присутній' THEN 1 END) as present_count
                FROM Child c
                LEFT JOIN Tabulation t ON c.ID = t.ID_child
                WHERE c.dateFinal IS NULL
                GROUP BY c.ID_group
            )
            SELECT 
                cg.ID,
                cg.nameGroup,
                e.fullname as teacher_name,
                e.ID as teacher_id,
                cg.capacity,
                COUNT(c.ID) as current_count,
                pc.cost as current_price,
                COALESCE(ga.present_count * 100.0 / NULLIF(ga.total_records, 0), 0) as attendance_percentage
            FROM ChildrensGroup cg
            JOIN Employee e ON cg.ID_teacher = e.ID
            LEFT JOIN Child c ON cg.ID = c.ID_group AND c.dateFinal IS NULL
            LEFT JOIN PriceChronology pc ON cg.ID = pc.ID_group AND pc.dateFinal IS NULL
            LEFT JOIN GroupAttendance ga ON cg.ID = ga.ID_group
            GROUP BY cg.ID, cg.nameGroup, e.fullname, e.ID, cg.capacity, pc.cost, 
                     ga.present_count, ga.total_records
            ORDER BY cg.nameGroup", conn))
                {
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        model.Groups.Add(new GroupViewModel
                        {
                            Id = reader.GetInt32(0),
                            Name = reader.GetString(1),
                            TeacherName = reader.GetString(2),
                            TeacherId = reader.GetInt32(3),
                            Capacity = reader.GetInt32(4),
                            CurrentCount = reader.GetInt32(5),
                            CurrentPrice = !reader.IsDBNull(6) ? reader.GetDecimal(6) : 0,
                            AttendancePercentage = !reader.IsDBNull(7) ? reader.GetDecimal(7) : 0,
                            Children = new List<ChildInGroupViewModel>()
                        });
                    }
                }

                // Получаем список всех воспитателей
                using (var cmd = new NpgsqlCommand(@"
            SELECT DISTINCT e.ID, e.fullname
            FROM Employee e
            JOIN EmployeeAndPost ep ON e.ID = ep.ID_employee
            JOIN Post p ON ep.ID_post = p.ID
            WHERE p.postTitle = 'Вихователь'
            AND ep.dateFinal IS NULL
            ORDER BY e.fullname", conn))
                {
                    var teachers = new List<SelectListItem>();
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        teachers.Add(new SelectListItem
                        {
                            Value = reader.GetInt32(0).ToString(),
                            Text = reader.GetString(1)
                        });
                    }
                    model.Teachers = teachers;
                }

                // Если выбрана группа, получаем статистику посещаемости
                if (model.SelectedGroupId.HasValue)
                {
                    var query = @"
                WITH DateSeries AS (
                    SELECT generate_series(@startDate::date, @endDate::date, '1 day'::interval)::date AS date
                ),
                DailyStats AS (
                    SELECT 
                        CASE 
                            WHEN @period = 'month' THEN date_trunc('month', d.date)::date
                            WHEN @period = 'week' THEN date_trunc('week', d.date)::date
                            ELSE d.date
                        END as grouped_date,
                        COUNT(CASE WHEN t.statusVisit = 'Присутній' THEN 1 END) as present_count,
                        COUNT(CASE WHEN t.statusVisit = 'Відсутній' THEN 1 END) as absent_count,
                        COUNT(CASE WHEN t.statusVisit = 'На лікарняному' THEN 1 END) as sick_count,
                        COUNT(CASE WHEN t.statusVisit = 'Вихідний' THEN 1 END) as weekend_count
                    FROM DateSeries d
                    LEFT JOIN Child c ON c.ID_group = @groupId AND 
                        (c.dateFinal IS NULL OR c.dateFinal >= d.date)
                    LEFT JOIN Tabulation t ON t.ID_child = c.ID AND t.dateVisit = d.date
                    GROUP BY grouped_date
                    ORDER BY grouped_date
                )
                SELECT * FROM DailyStats";

                    using (var cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("startDate", model.StartDate);
                        cmd.Parameters.AddWithValue("endDate", model.EndDate);
                        cmd.Parameters.AddWithValue("period", model.Period);
                        cmd.Parameters.AddWithValue("groupId", model.SelectedGroupId.Value);

                        var stats = new AttendanceStatsData();
                        using var reader = await cmd.ExecuteReaderAsync();

                        while (await reader.ReadAsync())
                        {
                            var date = reader.GetDateTime(0);
                            string label = model.Period switch
                            {
                                "month" => date.ToString("MM.yyyy"),
                                "week" => $"{date:dd.MM}-{date.AddDays(6):dd.MM}",
                                _ => date.ToString("dd.MM")
                            };

                            stats.Labels.Add(label);
                            stats.Present.Add(reader.GetInt32(1));
                            stats.Absent.Add(reader.GetInt32(2));
                            stats.Sick.Add(reader.GetInt32(3));
                            stats.Weekend.Add(reader.GetInt32(4));
                        }

                        // Находим максимальное значение для масштабирования графика
                        stats.MaxValue = new[] {
                    stats.Present.DefaultIfEmpty(0).Max(),
                    stats.Absent.DefaultIfEmpty(0).Max(),
                    stats.Sick.DefaultIfEmpty(0).Max(),
                    stats.Weekend.DefaultIfEmpty(0).Max()
                }.Max();

                        model.StatsData = stats;
                    }
                }
            }

            return View(model);
        }

        private async Task<AttendanceStatsData> GetAttendanceStats(NpgsqlConnection conn, int groupId,
            DateTime startDate, DateTime endDate, string period)
        {
            var stats = new AttendanceStatsData();
            var query = @"
        WITH DateSeries AS (
            SELECT generate_series(@startDate::date, @endDate::date, '1 day'::interval)::date AS date
        ),
        DailyStats AS (
            SELECT 
                CASE 
                    WHEN @period = 'month' THEN date_trunc('month', d.date)::date
                    WHEN @period = 'week' THEN date_trunc('week', d.date)::date
                    ELSE d.date
                END as grouped_date,
                COUNT(CASE WHEN t.statusVisit = 'Присутній' THEN 1 END) as present_count,
                COUNT(CASE WHEN t.statusVisit = 'Відсутній' THEN 1 END) as absent_count,
                COUNT(CASE WHEN t.statusVisit = 'На лікарняному' THEN 1 END) as sick_count,
                COUNT(CASE WHEN t.statusVisit = 'Вихідний' THEN 1 END) as weekend_count
            FROM DateSeries d
            LEFT JOIN Child c ON c.ID_group = @groupId AND 
                (c.dateFinal IS NULL OR c.dateFinal >= d.date)
            LEFT JOIN Tabulation t ON t.ID_child = c.ID AND t.dateVisit = d.date
            GROUP BY grouped_date
            ORDER BY grouped_date
        )
        SELECT * FROM DailyStats";

            using var cmd = new NpgsqlCommand(query, conn);
            cmd.Parameters.AddWithValue("groupId", groupId);
            cmd.Parameters.AddWithValue("startDate", startDate);
            cmd.Parameters.AddWithValue("endDate", endDate);
            cmd.Parameters.AddWithValue("period", period);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var date = reader.GetDateTime(0);
                string label = period switch
                {
                    "month" => date.ToString("MM.yyyy"),
                    "week" => $"{date:dd.MM}-{date.AddDays(6):dd.MM}",
                    _ => date.ToString("dd.MM")
                };

                stats.Labels.Add(label);
                stats.Present.Add(reader.GetInt32(1));
                stats.Absent.Add(reader.GetInt32(2));
                stats.Sick.Add(reader.GetInt32(3));
                stats.Weekend.Add(reader.GetInt32(4));
            }

            stats.MaxValue = Math.Max(
                Math.Max(stats.Present.DefaultIfEmpty(0).Max(), stats.Absent.DefaultIfEmpty(0).Max()),
                Math.Max(stats.Sick.DefaultIfEmpty(0).Max(), stats.Weekend.DefaultIfEmpty(0).Max())
            );

            return stats;
        }
        [HttpGet]
        public async Task<IActionResult> GetAttendanceData(string groupId, string period, DateTime startDate, DateTime endDate)
        {
            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            var stats = new AttendanceStatsData();

            string dateTruncCase;
            switch (period)
            {
                case "month":
                    dateTruncCase = "date_trunc('month', d.date)::date";
                    break;
                case "week":
                    dateTruncCase = "date_trunc('week', d.date)::date";
                    break;
                default:
                    dateTruncCase = "d.date";
                    break;
            }

            var query = $@"
        WITH DateSeries AS (
            SELECT generate_series(@startDate::date, @endDate::date, '1 day'::interval)::date AS date
        ),
        DailyStats AS (
            SELECT 
                {dateTruncCase} as grouped_date,
                COUNT(CASE WHEN t.statusVisit = 'Присутній' THEN 1 END) as present_count,
                COUNT(CASE WHEN t.statusVisit = 'Відсутній' THEN 1 END) as absent_count,
                COUNT(CASE WHEN t.statusVisit = 'На лікарняному' THEN 1 END) as sick_count,
                COUNT(CASE WHEN t.statusVisit = 'Вихідний' THEN 1 END) as weekend_count,
                COUNT(DISTINCT c.ID) as total_children
            FROM DateSeries d
            LEFT JOIN Child c ON (@groupId = 'all' OR c.ID_group = @groupId::integer) 
                AND (c.dateFinal IS NULL OR c.dateFinal >= d.date)
            LEFT JOIN Tabulation t ON t.ID_child = c.ID AND t.dateVisit = d.date
            WHERE c.ID IS NOT NULL 
            GROUP BY grouped_date
            HAVING COUNT(DISTINCT c.ID) > 0
            ORDER BY grouped_date
        )
        SELECT * FROM DailyStats";

            using var cmd = new NpgsqlCommand(query, conn);
            cmd.Parameters.AddWithValue("groupId", groupId);
            cmd.Parameters.AddWithValue("startDate", startDate);
            cmd.Parameters.AddWithValue("endDate", endDate);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var date = reader.GetDateTime(0);
                var totalChildren = reader.GetInt32(5);

                string label = period switch
                {
                    "month" => date.ToString("MM.yyyy"),
                    "week" => $"{date:dd.MM}-{date.AddDays(6):dd.MM}",
                    _ => date.ToString("dd.MM")
                };

                stats.Labels.Add(label);
                stats.Present.Add(reader.GetInt32(1));
                stats.Absent.Add(reader.GetInt32(2));
                stats.Sick.Add(reader.GetInt32(3));
                stats.Weekend.Add(reader.GetInt32(4));
            }

            // Если нет данных, создаем пустые метки для периода
            if (!stats.Labels.Any())
            {
                var current = startDate;
                while (current <= endDate)
                {
                    string label = period switch
                    {
                        "month" => current.ToString("MM.yyyy"),
                        "week" => $"{current:dd.MM}-{current.AddDays(6):dd.MM}",
                        _ => current.ToString("dd.MM")
                    };

                    stats.Labels.Add(label);
                    stats.Present.Add(0);
                    stats.Absent.Add(0);
                    stats.Sick.Add(0);
                    stats.Weekend.Add(0);

                    current = period switch
                    {
                        "month" => current.AddMonths(1),
                        "week" => current.AddDays(7),
                        _ => current.AddDays(1)
                    };
                }
            }

            stats.MaxValue = Math.Max(
                Math.Max(stats.Present.DefaultIfEmpty(0).Max(), stats.Absent.DefaultIfEmpty(0).Max()),
                Math.Max(stats.Sick.DefaultIfEmpty(0).Max(), stats.Weekend.DefaultIfEmpty(0).Max())
            );

            return Json(stats);
        }

        public async Task<IActionResult> CreateGroup()
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                // Получаем список воспитателей
                using var cmd = new NpgsqlCommand(@"
            SELECT e.ID, e.fullname
            FROM Employee e
            JOIN EmployeeAndPost ep ON e.ID = ep.ID_employee
            JOIN Post p ON ep.ID_post = p.ID
            WHERE p.postTitle = 'Вихователь'
            AND ep.dateFinal IS NULL
            ORDER BY e.fullname", conn);

                var teachers = new List<TeacherSelectListItem>();
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    teachers.Add(new TeacherSelectListItem
                    {
                        Value = reader.GetInt16(0),
                        Text = reader.GetString(1)
                    });
                }

                var model = new CreateGroupViewModel
                {
                    Teachers = teachers
                };

                return View(model);
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreateGroup(CreateGroupViewModel model)
        {
            if (string.IsNullOrEmpty(model.Name) || model.Capacity <= 0 || model.Price < 0)
            {
                return View(model);
            }

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using var transaction = await conn.BeginTransactionAsync();

                try
                {
                    // Создаем новую группу
                    Int16 groupId;
                    using (var cmd = new NpgsqlCommand(@"
                INSERT INTO ChildrensGroup (nameGroup, ID_teacher, capacity)
                VALUES (@name, @teacherId, @capacity)
                RETURNING ID", conn, transaction))
                    {
                        cmd.Parameters.AddWithValue("name", model.Name);
                        cmd.Parameters.AddWithValue("teacherId", (Int16)model.TeacherId);
                        cmd.Parameters.AddWithValue("capacity", (Int16)model.Capacity);
                        groupId = (Int16)await cmd.ExecuteScalarAsync();
                    }

                    // Создаем запись о цене
                    using (var cmd = new NpgsqlCommand(@"
                INSERT INTO PriceChronology (ID_group, cost, dateStart)
                VALUES (@groupId, @price, CURRENT_DATE)", conn, transaction))
                    {
                        cmd.Parameters.AddWithValue("groupId", groupId);
                        cmd.Parameters.AddWithValue("price", model.Price);
                        await cmd.ExecuteNonQueryAsync();
                    }

                    await transaction.CommitAsync();
                    return RedirectToAction(nameof(Groups));
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();

                    // Заново получаем список учителей
                    var teachers = new List<TeacherSelectListItem>();
                    using (var cmd = new NpgsqlCommand(@"
                SELECT e.ID, e.fullname
                FROM Employee e
                JOIN EmployeeAndPost ep ON e.ID = ep.ID_employee
                JOIN Post p ON ep.ID_post = p.ID
                WHERE p.postTitle = 'Вихователь'
                AND ep.dateFinal IS NULL
                ORDER BY e.fullname", conn))
                    {
                        using var reader = await cmd.ExecuteReaderAsync();
                        while (await reader.ReadAsync())
                        {
                            teachers.Add(new TeacherSelectListItem
                            {
                                Value = reader.GetInt16(0), // Изменено с GetInt32 на GetInt16
                                Text = reader.GetString(1)
                            });
                        }
                    }
                    model.Teachers = teachers;
                    ModelState.AddModelError("", "Помилка при створенні групи. Будь ласка, спробуйте ще раз.");
                    return View(model);
                }
            }
        }

        [HttpPost]
        public async Task<IActionResult> UpdateGroupName(int groupId, string name)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using var cmd = new NpgsqlCommand(@"
            UPDATE ChildrensGroup 
            SET nameGroup = @name 
            WHERE ID = @groupId", conn);

                cmd.Parameters.AddWithValue("groupId", groupId);
                cmd.Parameters.AddWithValue("name", name);

                try
                {
                    await cmd.ExecuteNonQueryAsync();
                    return Json(new { success = true });
                }
                catch (Exception)
                {
                    return Json(new { success = false, message = "Помилка при зміні назви групи" });
                }
            }
        }

        [HttpPost]
        public async Task<IActionResult> DeleteGroup(int groupId)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using var transaction = await conn.BeginTransactionAsync();

                try
                {
                    // Проверяем только наличие детей
                    using (var checkCmd = new NpgsqlCommand(@"
                SELECT EXISTS(
                    SELECT 1 FROM Child 
                    WHERE ID_group = @id 
                    AND dateFinal IS NULL)", conn))
                    {
                        checkCmd.Parameters.AddWithValue("id", groupId);
                        var hasChildren = (bool)await checkCmd.ExecuteScalarAsync();

                        if (hasChildren)
                        {
                            return Json(new
                            {
                                success = false,
                                message = "Неможливо видалити групу, оскільки в ній є діти"
                            });
                        }
                    }

                    // Сначала удаляем записи из PriceChronology
                    using (var priceCmd = new NpgsqlCommand(
                        "DELETE FROM PriceChronology WHERE ID_group = @id", conn, transaction))
                    {
                        priceCmd.Parameters.AddWithValue("id", groupId);
                        await priceCmd.ExecuteNonQueryAsync();
                    }

                    // Затем удаляем саму группу
                    using (var groupCmd = new NpgsqlCommand(
                        "DELETE FROM ChildrensGroup WHERE ID = @id", conn, transaction))
                    {
                        groupCmd.Parameters.AddWithValue("id", groupId);
                        await groupCmd.ExecuteNonQueryAsync();
                    }

                    await transaction.CommitAsync();
                    return Json(new { success = true });
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync();
                    return Json(new
                    {
                        success = false,
                        message = "Помилка при видаленні групи"
                    });
                }
            }
        }



        [HttpGet]
        public async Task<IActionResult> Children(string searchTerm = null, int? groupId = null)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                var model = new FamilySearchViewModel
                {
                    SearchTerm = searchTerm,
                    SelectedGroupId = groupId,
                    Children = new List<ChildWithFamilyViewModel>(),
                    Groups = new List<GroupViewModel>()
                };

                // Получаем список групп
                using (var cmdGroups = new NpgsqlCommand(@"
            SELECT id, nameGroup 
            FROM ChildrensGroup 
            ORDER BY nameGroup", conn))
                {
                    using var reader = await cmdGroups.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        model.Groups.Add(new GroupViewModel
                        {
                            Id = reader.GetInt32(0),
                            Name = reader.GetString(1)
                        });
                    }
                }

                // Получаем детей с информацией о семьях и группах
                string query = @"
            SELECT 
                c.id,
                c.fullname as child_name,
                cg.nameGroup as group_name,
                e.fullname as teacher_name,
                f.fullnameResponsible as responsible_name,
                f.phoneResponsible as responsible_phone,
                c.dateStart,
                string_agg(DISTINCT con.name, ', ') as contraindications
            FROM Child c
            JOIN Family f ON c.ID_family = f.ID
            JOIN ChildrensGroup cg ON c.ID_group = cg.ID
            JOIN Employee e ON cg.ID_teacher = e.ID
            LEFT JOIN ContraindChildren cc ON c.ID = cc.ID_child
            LEFT JOIN Contraindications con ON cc.ID_contraind = con.ID
            WHERE c.dateFinal IS NULL ";

                if (groupId.HasValue)
                {
                    query += " AND c.ID_group = @groupId";
                }
                if (!string.IsNullOrEmpty(searchTerm))
                {
                    query += @" AND (c.fullname ILIKE @searchPattern 
                    OR f.fullnameResponsible ILIKE @searchPattern)";
                }

                query += @" GROUP BY 
                c.id, c.fullname, cg.nameGroup, e.fullname,
                f.fullnameResponsible, f.phoneResponsible, c.dateStart
            ORDER BY c.fullname";

                using (var cmdChildren = new NpgsqlCommand(query, conn))
                {
                    if (!string.IsNullOrEmpty(searchTerm))
                    {
                        cmdChildren.Parameters.AddWithValue("searchPattern", $"%{searchTerm}%");
                    }
                    if (groupId.HasValue)
                    {
                        cmdChildren.Parameters.AddWithValue("groupId", groupId.Value);
                    }

                    using var reader = await cmdChildren.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var child = new ChildWithFamilyViewModel
                        {
                            Id = reader.GetInt32(0),
                            FullName = reader.GetString(1),
                            GroupName = reader.GetString(2),
                            TeacherName = reader.GetString(3),
                            MainResponsibleName = reader.GetString(4),
                            MainResponsiblePhone = reader.GetString(5),
                            StartDate = reader.GetDateTime(6),
                            Contraindications = !reader.IsDBNull(7)
                                ? reader.GetString(7).Split(", ").ToList()
                                : new List<string>()
                        };
                        model.Children.Add(child);
                    }
                }

                return View(model);
            }
        }

        // Метод для получения существующей семьи
        public async Task<IActionResult> GetFamily(int id)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                var model = new FamilyViewModel();

                // Получаем основную информацию о семье
                using (var cmd = new NpgsqlCommand(@"
            SELECT fullnameResponsible, phoneResponsible, placeOfResidence
            FROM Family 
            WHERE id = @id", conn))
                {
                    cmd.Parameters.AddWithValue("id", id);
                    using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        model.Id = id;
                        model.ResponsibleName = reader.GetString(0);
                        model.PhoneResponsible = reader.GetString(1);
                        model.PlaceOfResidence = reader.GetString(2);
                    }
                }

                // Получаем дополнительных ответственных
                using (var cmd = new NpgsqlCommand(@"
            SELECT id, fullname, phone
            FROM Responsible
            WHERE ID_family = @familyId", conn))
                {
                    cmd.Parameters.AddWithValue("familyId", id);
                    using var reader = await cmd.ExecuteReaderAsync();
                    model.AdditionalResponsibles = new List<ResponsibleViewModel>();
                    while (await reader.ReadAsync())
                    {
                        model.AdditionalResponsibles.Add(new ResponsibleViewModel
                        {
                            Id = reader.GetInt32(0),
                            Fullname = reader.GetString(1),
                            Phone = reader.GetString(2)
                        });
                    }
                }

                return View(model);
            }
        }

        [HttpPost]
        public async Task<IActionResult> UpdateFamily(FamilyViewModel model)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using var transaction = await conn.BeginTransactionAsync();

                try
                {
                    // Обновляем основную информацию
                    using (var cmd = new NpgsqlCommand(@"
                UPDATE Family 
                SET fullnameResponsible = @fullname, 
                    phoneResponsible = @phone,
                    placeOfResidence = @address
                WHERE id = @id", conn, transaction))
                    {
                        cmd.Parameters.AddWithValue("id", model.Id);
                        cmd.Parameters.AddWithValue("fullname", model.ResponsibleName);
                        cmd.Parameters.AddWithValue("phone", model.PhoneResponsible);
                        cmd.Parameters.AddWithValue("address", model.PlaceOfResidence);
                        await cmd.ExecuteNonQueryAsync();
                    }

                    await transaction.CommitAsync();
                    return RedirectToAction("Families");
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    ModelState.AddModelError("", $"Помилка при оновленні сім'ї: {ex.Message}");
                    return View(model);
                }
            }
        }



        //// Метод для сохранения новой семьи
        //[HttpPost]
        //public async Task<IActionResult> CreateFamily(FamilyViewModel model)
        //{
        //    if (!ModelState.IsValid)
        //        return View(model);

        //    using (var conn = new NpgsqlConnection(_connectionString))
        //    {
        //        await conn.OpenAsync();
        //        using var transaction = await conn.BeginTransactionAsync();

        //        try
        //        {
        //            // Добавляем основную семью
        //            int familyId;
        //            using (var cmd = new NpgsqlCommand(@"
        //        INSERT INTO Family (fullnameResponsible, phoneResponsible, placeOfResidence)
        //        VALUES (@name, @phone, @address)
        //        RETURNING id", conn, transaction))
        //            {
        //                cmd.Parameters.AddWithValue("name", model.ResponsibleName);
        //                cmd.Parameters.AddWithValue("phone", model.PhoneResponsible);
        //                cmd.Parameters.AddWithValue("address", model.PlaceOfResidence);
        //                familyId = (int)await cmd.ExecuteScalarAsync();
        //            }

        //            // Добавляем дополнительных ответственных лиц
        //            if (model.AdditionalResponsibles?.Any() == true)
        //            {
        //                foreach (var responsible in model.AdditionalResponsibles)
        //                {
        //                    using var cmd = new NpgsqlCommand(@"
        //                INSERT INTO Responsible (ID_family, fullname, phone)
        //                VALUES (@familyId, @fullname, @phone)", conn, transaction);
        //                    cmd.Parameters.AddWithValue("familyId", familyId);
        //                    cmd.Parameters.AddWithValue("fullname", responsible.Fullname);
        //                    cmd.Parameters.AddWithValue("phone", responsible.Phone);
        //                    await cmd.ExecuteNonQueryAsync();
        //                }
        //            }

        //            await transaction.CommitAsync();
        //            return RedirectToAction("Children");
        //        }
        //        catch (Exception ex)
        //        {
        //            await transaction.RollbackAsync();
        //            ModelState.AddModelError("", $"Помилка при створенні сім'ї: {ex.Message}");
        //            return View(model);
        //        }
        //    }
        //}

        // Метод для перевода ребенка в другую группу
        [HttpPost]
        public async Task<IActionResult> TransferChild([FromBody] TransferChildModel model)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using var transaction = await conn.BeginTransactionAsync();

                try
                {
                    int familyId;
                    string fullName;

                    // 1. Получаем текущие данные о ребенке
                    using (var cmdGetChild = new NpgsqlCommand(@"
                SELECT ID_family, fullname 
                FROM Child 
                WHERE ID = @childId AND dateFinal IS NULL", conn, transaction))
                    {
                        cmdGetChild.Parameters.AddWithValue("childId", NpgsqlDbType.Integer, model.ChildId);

                        using var reader = await cmdGetChild.ExecuteReaderAsync();
                        if (!await reader.ReadAsync())
                        {
                            return Json(new { success = false, message = "Дитину не знайдено" });
                        }

                        familyId = reader.GetInt32(0);
                        fullName = reader.GetString(1);
                    }

                    // 2. Закрываем текущую запись
                    using (var cmdCloseOld = new NpgsqlCommand(@"
                UPDATE Child 
                SET dateFinal = CURRENT_DATE
                WHERE ID = @childId AND dateFinal IS NULL", conn, transaction))
                    {
                        cmdCloseOld.Parameters.AddWithValue("childId", NpgsqlDbType.Integer, model.ChildId);
                        await cmdCloseOld.ExecuteNonQueryAsync();
                    }

                    // 3. Создаем новую запись в новой группе
                    using (var cmdNewRecord = new NpgsqlCommand(@"
                INSERT INTO Child (ID_family, ID_group, fullname, dateStart)
                VALUES (@familyId, @groupId, @fullName, CURRENT_DATE)", conn, transaction))
                    {
                        cmdNewRecord.Parameters.AddWithValue("familyId", NpgsqlDbType.Integer, familyId);
                        cmdNewRecord.Parameters.AddWithValue("groupId", NpgsqlDbType.Integer, model.NewGroupId);
                        cmdNewRecord.Parameters.AddWithValue("fullName", NpgsqlDbType.Varchar, fullName);
                        await cmdNewRecord.ExecuteNonQueryAsync();
                    }

                    await transaction.CommitAsync();
                    return Json(new { success = true });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    return Json(new { success = false, message = ex.Message });
                }
            }
        }


        [HttpGet]
        public async Task<IActionResult> ChildDetails(int id)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                var model = new FamilyViewModel();

                // Получаем основную информацию о ребенке и семье
                using (var cmd = new NpgsqlCommand(@"
            SELECT 
                c.id,
                c.fullname as child_name,
                cg.nameGroup,
                e.fullname as teacher_name,
                f.id as family_id,
                f.fullnameResponsible,
                f.phoneResponsible,
                f.placeOfResidence,
                c.dateStart,
                string_agg(DISTINCT con.name, ', ') as contraindications
            FROM Child c
            JOIN Family f ON c.ID_family = f.ID
            JOIN ChildrensGroup cg ON c.ID_group = cg.ID
            JOIN Employee e ON cg.ID_teacher = e.ID
            LEFT JOIN ContraindChildren cc ON c.ID = cc.ID_child
            LEFT JOIN Contraindications con ON cc.ID_contraind = con.ID
            WHERE c.ID = @childId AND c.dateFinal IS NULL
            GROUP BY c.id, c.fullname, cg.nameGroup, e.fullname, 
                     f.id, f.fullnameResponsible, f.phoneResponsible, f.placeOfResidence, c.dateStart", conn))
                {
                    cmd.Parameters.AddWithValue("childId", id);

                    using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        model.Id = reader.GetInt32(4); // family_id
                        model.ResponsibleName = reader.GetString(5);
                        model.PhoneResponsible = reader.GetString(6);
                        model.PlaceOfResidence = reader.GetString(7);

                        // Инициализируем список детей
                        model.Children = new List<ChildViewModel>
                {
                    new ChildViewModel
                    {
                        Id = reader.GetInt32(0),
                        FullName = reader.GetString(1),
                        GroupName = reader.GetString(2),
                        StartDate = reader.GetDateTime(8),
                        Contraindications = !reader.IsDBNull(9)
                            ? reader.GetString(9).Split(", ").ToList()
                            : new List<string>()
                    }
                };
                    }
                }

                // Получаем дополнительных ответственных лиц
                using (var cmd = new NpgsqlCommand(@"
            SELECT id, fullname, phone
            FROM Responsible
            WHERE ID_family = @familyId
            ORDER BY ID", conn))
                {
                    cmd.Parameters.AddWithValue("familyId", model.Id);
                    model.AdditionalResponsibles = new List<ResponsibleViewModel>();

                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        model.AdditionalResponsibles.Add(new ResponsibleViewModel
                        {
                            Id = reader.GetInt32(0),
                            Fullname = reader.GetString(1),
                            Phone = reader.GetString(2)
                        });
                    }
                }

                return View(model);
            }
        }

        public async Task<IActionResult> Families(string searchTerm = null)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                var model = new FamilyManagementViewModel
                {
                    SearchTerm = searchTerm
                };

                // Изменяем запрос, чтобы избежать проблем с типом параметра
                var query = searchTerm != null
                    ? @"
                SELECT 
                    f.ID,
                    f.fullnameResponsible,
                    f.phoneResponsible,
                    f.placeOfResidence
                FROM Family f
                WHERE f.fullnameResponsible ILIKE '%' || @searchTerm || '%'
                ORDER BY f.fullnameResponsible"
                    : @"
                SELECT 
                    f.ID,
                    f.fullnameResponsible,
                    f.phoneResponsible,
                    f.placeOfResidence
                FROM Family f
                ORDER BY f.fullnameResponsible";

                using (var cmd = new NpgsqlCommand(query, conn))
                {
                    if (searchTerm != null)
                    {
                        cmd.Parameters.AddWithValue("searchTerm", searchTerm);
                    }

                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var family = new FamilyViewModel
                        {
                            Id = reader.GetInt32(0),
                            ResponsibleName = reader.GetString(1),
                            PhoneResponsible = reader.GetString(2),
                            PlaceOfResidence = reader.GetString(3),
                            AdditionalResponsibles = new List<ResponsibleViewModel>(),
                            Children = new List<ChildViewModel>()
                        };
                        model.Families.Add(family);
                    }
                }

                // Дополнительная информация для каждой семьи
                foreach (var family in model.Families)
                {
                    // Дополнительные ответственные
                    using (var cmd = new NpgsqlCommand(@"
                SELECT id, fullname, phone
                FROM Responsible
                WHERE ID_family = @familyId
                ORDER BY id", conn))
                    {
                        cmd.Parameters.AddWithValue("familyId", family.Id);
                        using var reader = await cmd.ExecuteReaderAsync();
                        while (await reader.ReadAsync())
                        {
                            family.AdditionalResponsibles.Add(new ResponsibleViewModel
                            {
                                Id = reader.GetInt32(0),
                                Fullname = reader.GetString(1),
                                Phone = reader.GetString(2)
                            });
                        }
                    }

                    // Дети
                    using (var cmd = new NpgsqlCommand(@"
                SELECT 
                    c.id,
                    c.fullname,
                    cg.nameGroup,
                    c.dateStart,
                    c.dateFinal
                FROM Child c
                JOIN ChildrensGroup cg ON c.ID_group = cg.ID
                WHERE c.ID_family = @familyId
                AND c.dateFinal IS NULL
                ORDER BY c.fullname", conn))
                    {
                        cmd.Parameters.AddWithValue("familyId", family.Id);
                        using var reader = await cmd.ExecuteReaderAsync();
                        while (await reader.ReadAsync())
                        {
                            family.Children.Add(new ChildViewModel
                            {
                                Id = reader.GetInt32(0),
                                FullName = reader.GetString(1),
                                GroupName = reader.GetString(2),
                                StartDate = reader.GetDateTime(3),
                                EndDate = !reader.IsDBNull(4) ? reader.GetDateTime(4) : null
                            });
                        }
                    }
                }

                return View(model);
            }
        }

        [HttpGet]
        public IActionResult CreateFamily()
        {
            return View(new CreateFamilyViewModel());
        }

        [HttpPost]
        public async Task<IActionResult> CreateFamily(CreateFamilyViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using var transaction = await conn.BeginTransactionAsync();

                try
                {
                    using (var cmd = new NpgsqlCommand(@"
                INSERT INTO Family (fullnameResponsible, phoneResponsible, placeOfResidence)
                VALUES (@name, @phone, @address)
                RETURNING id", conn, transaction))
                    {
                        cmd.Parameters.AddWithValue("name", model.ResponsibleName);
                        cmd.Parameters.AddWithValue("phone", model.PhoneResponsible);
                        cmd.Parameters.AddWithValue("address", model.PlaceOfResidence);
                        await cmd.ExecuteScalarAsync();
                    }

                    await transaction.CommitAsync();
                    TempData["Success"] = "Сім'ю успішно створено";
                    return RedirectToAction("Families");
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    ModelState.AddModelError("", $"Помилка при створенні сім'ї: {ex.Message}");
                    return View(model);
                }
            }
        }



        [HttpGet]
        public async Task<IActionResult> EditFamily(int id)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                var model = new EditFamilyViewModel();

                // Получаем основную информацию о семье
                using (var cmd = new NpgsqlCommand(@"
            SELECT fullnameResponsible, phoneResponsible, placeOfResidence 
            FROM Family 
            WHERE id = @id", conn))
                {
                    cmd.Parameters.AddWithValue("id", id);
                    using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        model.Id = id;
                        model.ResponsibleName = reader.GetString(0);
                        model.PhoneResponsible = reader.GetString(1);
                        model.PlaceOfResidence = reader.GetString(2);
                    }
                }

                return View(model);
            }
        }

        [HttpPost]
        public async Task<IActionResult> EditFamily(EditFamilyViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using var transaction = await conn.BeginTransactionAsync();

                try
                {
                    using (var cmd = new NpgsqlCommand(@"
                UPDATE Family 
                SET fullnameResponsible = @name, 
                    phoneResponsible = @phone, 
                    placeOfResidence = @address
                WHERE id = @id", conn, transaction))
                    {
                        cmd.Parameters.AddWithValue("id", model.Id);
                        cmd.Parameters.AddWithValue("name", model.ResponsibleName);
                        cmd.Parameters.AddWithValue("phone", model.PhoneResponsible);
                        cmd.Parameters.AddWithValue("address", model.PlaceOfResidence);
                        await cmd.ExecuteNonQueryAsync();
                    }

                    await transaction.CommitAsync();
                    TempData["Success"] = "Дані успішно оновлено";
                    return RedirectToAction("Families");
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    ModelState.AddModelError("", $"Помилка при оновленні даних: {ex.Message}");
                    return View(model);
                }
            }
        }


        [HttpGet]
        [Route("[controller]/AddChild/{familyId}")]
        public async Task<IActionResult> AddChild(int familyId)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                // Проверяем существование семьи
                using (var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM Family WHERE ID = @familyId", conn))
                {
                    cmd.Parameters.AddWithValue("familyId", familyId);
                    var count = (long)await cmd.ExecuteScalarAsync();
                    if (count == 0)
                    {
                        TempData["Error"] = "Сім'ю не знайдено";
                        return RedirectToAction("Families");
                    }
                }

                var model = new AddChildViewModel { FamilyId = familyId };
                await LoadChildFormData(conn, model);
                return View(model);
            }
        }

        [HttpPost]
        public async Task<IActionResult> AddChild(AddChildViewModel model)
        {
            if (!ModelState.IsValid)
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    await LoadChildFormData(conn, model);
                    return View(model);
                }
            }

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                // Проверяем существование семьи
                using (var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM Family WHERE ID = @familyId", conn))
                {
                    cmd.Parameters.AddWithValue("familyId", model.FamilyId);
                    var count = (long)await cmd.ExecuteScalarAsync();
                    if (count == 0)
                    {
                        ModelState.AddModelError("", "Сім'ю не знайдено");
                        await LoadChildFormData(conn, model);
                        return View(model);
                    }
                }

                // Проверяем существование группы
                using (var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM ChildrensGroup WHERE ID = @groupId", conn))
                {
                    cmd.Parameters.AddWithValue("groupId", model.GroupId);
                    var count = (long)await cmd.ExecuteScalarAsync();
                    if (count == 0)
                    {
                        ModelState.AddModelError("", "Групу не знайдено");
                        await LoadChildFormData(conn, model);
                        return View(model);
                    }
                }

                using var transaction = await conn.BeginTransactionAsync();

                try
                {
                    // Проверяем наличие свободных мест в группе
                    using (var cmd = new NpgsqlCommand(@"
                    SELECT 
                        CAST(cg.capacity AS int) - 
                        CAST(COUNT(CASE WHEN c.dateFinal IS NULL THEN 1 END) AS int) as free_places
                    FROM ChildrensGroup cg
                    LEFT JOIN Child c ON cg.id = c.id_group
                    WHERE cg.id = @groupId
                    GROUP BY cg.capacity", conn, transaction))
                    {
                        cmd.Parameters.AddWithValue("groupId", model.GroupId);
                        var result = await cmd.ExecuteScalarAsync();
                        var freePlaces = Convert.ToInt32(result);

                        if (freePlaces <= 0)
                        {
                            ModelState.AddModelError("GroupId", "У вибраній групі немає вільних місць");
                            await LoadChildFormData(conn, model);
                            return View(model);
                        }
                    }

                    // Добавляем ребенка
                    int childId;
                    using (var cmd = new NpgsqlCommand(@"
                INSERT INTO Child (ID_family, ID_group, fullname, dateStart)
                VALUES (@familyId, @groupId, @fullName, @startDate)
                RETURNING id", conn, transaction))
                    {
                        cmd.Parameters.AddWithValue("familyId", model.FamilyId);
                        cmd.Parameters.AddWithValue("groupId", model.GroupId);
                        cmd.Parameters.AddWithValue("fullName", model.FullName);
                        cmd.Parameters.AddWithValue("startDate", model.StartDate);
                        childId = (int)await cmd.ExecuteScalarAsync();
                    }

                    // Добавляем противопоказания
                    if (model.SelectedContraindications?.Any() == true)
                    {
                        foreach (var contraindId in model.SelectedContraindications)
                        {
                            using var cmd = new NpgsqlCommand(@"
                        INSERT INTO ContraindChildren (ID_contraind, ID_child)
                        VALUES (@contraindId, @childId)", conn, transaction);
                            cmd.Parameters.AddWithValue("contraindId", contraindId);
                            cmd.Parameters.AddWithValue("childId", childId);
                            await cmd.ExecuteNonQueryAsync();
                        }
                    }

                    await transaction.CommitAsync();
                    TempData["Success"] = "Дитину успішно додано";
                    return RedirectToAction("Families");
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    ModelState.AddModelError("", $"Помилка при додаванні дитини: {ex.Message}");
                    await LoadChildFormData(conn, model);
                    return View(model);
                }
            }
        }

        private async Task LoadChildFormData(NpgsqlConnection conn, AddChildViewModel model)
        {
            // Загружаем список групп
            using (var cmd = new NpgsqlCommand(@"
        SELECT 
            cg.id,
            cg.nameGroup,
            CAST(cg.capacity AS int) - 
            CAST(COUNT(CASE WHEN c.dateFinal IS NULL THEN 1 END) AS int) as free_places
        FROM ChildrensGroup cg
        LEFT JOIN Child c ON cg.id = c.id_group
        GROUP BY cg.id, cg.nameGroup, cg.capacity
        HAVING CAST(cg.capacity AS int) - 
               CAST(COUNT(CASE WHEN c.dateFinal IS NULL THEN 1 END) AS int) > 0
        ORDER BY cg.nameGroup", conn))
            {
                var groups = new List<SelectListItem>();
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var groupId = reader.GetInt16(0);
                    var groupName = reader.GetString(1);
                    var freePlaces = reader.GetInt32(2);

                    groups.Add(new SelectListItem
                    {
                        Value = groupId.ToString(),
                        Text = $"{groupName} (вільних місць: {freePlaces})"
                    });
                }
                model.Groups = new SelectList(groups, "Value", "Text", model.GroupId);
            }

            // Загружаем список противопоказаний
            using (var cmd = new NpgsqlCommand(@"
        SELECT id, name 
        FROM Contraindications 
        ORDER BY name", conn))
            {
                var contraindications = new List<SelectListItem>();
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    contraindications.Add(new SelectListItem
                    {
                        Value = reader.GetInt32(0).ToString(),
                        Text = reader.GetString(1)
                    });
                }
                model.AvailableContraindications = new MultiSelectList(
                    contraindications,
                    "Value",
                    "Text",
                    model.SelectedContraindications
                );
            }
        }



        [HttpGet]
        [Route("[controller]/AddResponsible/{familyId}")]
        public async Task<IActionResult> AddResponsible(int familyId)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                // Проверяем существование семьи и получаем информацию
                using (var cmd = new NpgsqlCommand(@"
            SELECT id, fullnameResponsible 
            FROM Family 
            WHERE id = @familyId", conn))
                {
                    cmd.Parameters.AddWithValue("familyId", familyId);
                    using var reader = await cmd.ExecuteReaderAsync();

                    if (await reader.ReadAsync())
                    {
                        var model = new AddResponsibleViewModel
                        {
                            FamilyId = familyId
                        };
                        ViewBag.FamilyName = reader.GetString(1);
                        return View(model);
                    }
                }

                TempData["Error"] = "Сім'ю не знайдено";
                return RedirectToAction("Families");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddResponsible(AddResponsibleViewModel model)
        {
            if (!ModelState.IsValid)
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new NpgsqlCommand("SELECT fullnameResponsible FROM Family WHERE id = @familyId", conn))
                    {
                        cmd.Parameters.AddWithValue("familyId", model.FamilyId);
                        ViewBag.FamilyName = (string)await cmd.ExecuteScalarAsync();
                    }
                }
                return View(model);
            }

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using var transaction = await conn.BeginTransactionAsync();

                try
                {
                    // Проверяем уникальность телефона
                    using (var cmd = new NpgsqlCommand(@"
                SELECT COUNT(*) 
                FROM Responsible 
                WHERE phone = @phone", conn, transaction))
                    {
                        cmd.Parameters.AddWithValue("phone", model.Phone);
                        var count = (long)await cmd.ExecuteScalarAsync();
                        if (count > 0)
                        {
                            ModelState.AddModelError("Phone", "Такий номер телефону вже зареєстровано");
                            return View(model);
                        }
                    }

                    // Добавляем ответственное лицо
                    using (var cmd = new NpgsqlCommand(@"
                INSERT INTO Responsible (ID_family, fullname, phone)
                VALUES (@familyId, @fullname, @phone)", conn, transaction))
                    {
                        cmd.Parameters.AddWithValue("familyId", model.FamilyId);
                        cmd.Parameters.AddWithValue("fullname", model.Fullname);
                        cmd.Parameters.AddWithValue("phone", model.Phone);
                        await cmd.ExecuteNonQueryAsync();
                    }

                    await transaction.CommitAsync();
                    TempData["Success"] = "Відповідальну особу успішно додано";
                    return RedirectToAction("Families");
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    ModelState.AddModelError("", $"Помилка при додаванні відповідальної особи: {ex.Message}");
                    using (var cmd = new NpgsqlCommand("SELECT fullnameResponsible FROM Family WHERE id = @familyId", conn))
                    {
                        cmd.Parameters.AddWithValue("familyId", model.FamilyId);
                        ViewBag.FamilyName = (string)await cmd.ExecuteScalarAsync();
                    }
                    return View(model);
                }
            }
        }
    }
}