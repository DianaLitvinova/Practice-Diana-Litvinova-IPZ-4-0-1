using Diana_Litvinova_IPZ_4_0_1.Models;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace Diana_Litvinova_IPZ_4_0_1.Controllers
{
    public class CookController : Controller
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private string _connectionString => _httpContextAccessor.HttpContext?.Session.GetString("ConnectionString");

        public CookController(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public IActionResult Index()
        {
            if (HttpContext.Session.GetString("Role") != "cook")
                return RedirectToAction("Login", "Account");
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> GetGroupContraindications(int id)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using var cmd = new NpgsqlCommand(@"
                    SELECT 
                        g.nameGroup as groupName,
                        json_agg(json_build_object(
                            'fullname', c.fullname,
                            'contraindications', (
                                SELECT json_agg(con.name)
                                FROM ContraindChildren cc
                                JOIN Contraindications con ON cc.ID_contraind = con.ID
                                WHERE cc.ID_child = c.ID
                            ),
                            'forbiddenProducts', (
                                SELECT json_agg(DISTINCT cp.name)
                                FROM ContraindChildren cc
                                JOIN ContraindWithProducts cwp ON cc.ID_contraind = cwp.ID_contraind
                                JOIN ContraindicatedProducts cp ON cwp.ID_contraindProd = cp.ID
                                WHERE cc.ID_child = c.ID
                            )
                        )) as children
                    FROM ChildrensGroup g
                    JOIN Child c ON g.ID = c.ID_group
                    WHERE g.ID = @id AND c.dateFinal IS NULL
                    GROUP BY g.nameGroup", conn);

                cmd.Parameters.AddWithValue("id", id);

                object result = await cmd.ExecuteScalarAsync();
                if (result == null)
                {
                    return NotFound();
                }

                return Json(result);
            }
        }

        public async Task<IActionResult> GroupContraindications()
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using var cmd = new NpgsqlCommand(@"
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
            ORDER BY cg.nameGroup", conn);

                var groups = new List<GroupViewModel>();
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

                return View(groups);
            }
        }

        public async Task<IActionResult> GroupDetails(int id)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                // Получаем основную информацию о группе
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
            LEFT JOIN PriceChronology pc ON cg.ID = pc.ID_group AND pc.dateFinal IS NULL
            WHERE cg.ID = @id", conn))
                {
                    cmd.Parameters.AddWithValue("id", id);
                    using var reader = await cmd.ExecuteReaderAsync();

                    var group = new GroupViewModel();
                    if (await reader.ReadAsync())
                    {
                        group.Id = reader.GetInt32(0);
                        group.Name = reader.GetString(1);
                        group.TeacherName = reader.GetString(2);
                        group.TeacherId = reader.GetInt32(3);
                        group.Capacity = reader.GetInt32(4);
                        group.CurrentPrice = !reader.IsDBNull(5) ? reader.GetDecimal(5) : 0;
                        group.Children = new List<ChildInGroupViewModel>();
                    }
                    else
                    {
                        return NotFound();
                    }

                    // Получаем список детей с противопоказаниями
                    using (var childCmd = new NpgsqlCommand(@"
                SELECT 
                    c.ID,
                    c.fullname,
                    f.fullnameResponsible as family_name,
                    c.dateStart,
                    c.dateFinal,
                    STRING_AGG(DISTINCT con.name, ', ') as contraindications,
                    t.statusVisit,
                    COALESCE(
                        (SELECT COUNT(*) FILTER (WHERE t2.statusVisit = 'Присутній') * 100.0 / 
                         NULLIF(COUNT(*), 0)
                         FROM Tabulation t2 
                         WHERE t2.ID_child = c.ID), 0
                    ) as attendance_percentage
                FROM Child c
                JOIN Family f ON c.ID_family = f.ID
                LEFT JOIN ContraindChildren cc ON c.ID = cc.ID_child
                LEFT JOIN Contraindications con ON cc.ID_contraind = con.ID
                LEFT JOIN Tabulation t ON c.ID = t.ID_child 
                    AND t.dateVisit = CURRENT_DATE
                WHERE c.ID_group = @id AND c.dateFinal IS NULL
                GROUP BY c.ID, c.fullname, f.fullnameResponsible, c.dateStart, 
                         c.dateFinal, t.statusVisit", conn))
                    {
                        childCmd.Parameters.AddWithValue("id", id);
                        using var childReader = await childCmd.ExecuteReaderAsync();
                        while (await childReader.ReadAsync())
                        {
                            group.Children.Add(new ChildInGroupViewModel
                            {
                                Id = childReader.GetInt32(0),
                                FullName = childReader.GetString(1),
                                FamilyName = childReader.GetString(2),
                                StartDate = childReader.GetDateTime(3),
                                EndDate = !childReader.IsDBNull(4) ? childReader.GetDateTime(4) : null,
                                Contraindications = !childReader.IsDBNull(5)
                                    ? childReader.GetString(5).Split(", ").ToList()
                                    : new List<string>(),
                                AttendanceStatus = !childReader.IsDBNull(6) ? childReader.GetString(6) : "Відсутній",
                                AttendancePercentage = childReader.GetDecimal(7)
                            });
                        }
                    }

                    group.CurrentCount = group.Children.Count;
                    return View(group);
                }
            }
        }

        // Основной метод для отображения противопоказаний
        [HttpGet]
        [Route("Cook/GroupContraindications")]
        public async Task<IActionResult> GroupContraindications(string view = "all")
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                if (view == "all")
                {
                    // Запрос для общего списка
                    using var cmd = new NpgsqlCommand(@"
                        SELECT 
                            c.fullname as child_name,
                            cg.nameGroup as group_name,
                            STRING_AGG(DISTINCT con.name, ', ') as contraindications,
                            STRING_AGG(DISTINCT cp.name, ', ') as forbidden_products
                        FROM Child c
                        JOIN ChildrensGroup cg ON c.ID_group = cg.ID
                        LEFT JOIN ContraindChildren cc ON c.ID = cc.ID_child
                        LEFT JOIN Contraindications con ON cc.ID_contraind = con.ID
                        LEFT JOIN ContraindWithProducts cwp ON con.ID = cwp.ID_contraind
                        LEFT JOIN ContraindicatedProducts cp ON cwp.ID_contraindProd = cp.ID
                        WHERE c.dateFinal IS NULL
                        GROUP BY c.fullname, cg.nameGroup
                        ORDER BY cg.nameGroup, c.fullname", conn);

                    var allContraindications = new List<ChildContraindicationsViewModel>();
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        allContraindications.Add(new ChildContraindicationsViewModel
                        {
                            ChildName = reader.GetString(0),
                            GroupName = reader.GetString(1),
                            Contraindications = !reader.IsDBNull(2)
                                ? reader.GetString(2).Split(", ").ToList()
                                : new List<string>(),
                            ForbiddenProducts = !reader.IsDBNull(3)
                                ? reader.GetString(3).Split(", ").ToList()
                                : new List<string>()
                        });
                    }

                    ViewBag.View = "all";
                    return View(allContraindications);
                }
                else
                {
                    // Запрос для групп
                    using var cmd = new NpgsqlCommand(@"
                        SELECT 
                            cg.ID,
                            cg.nameGroup,
                            e.fullname as teacher_name,
                            COUNT(DISTINCT c.ID) as children_count
                        FROM ChildrensGroup cg
                        JOIN Employee e ON cg.ID_teacher = e.ID
                        LEFT JOIN Child c ON cg.ID = c.ID_group AND c.dateFinal IS NULL
                        GROUP BY cg.ID, cg.nameGroup, e.fullname
                        ORDER BY cg.nameGroup", conn);

                    var groups = new List<GroupViewModel>();
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        groups.Add(new GroupViewModel
                        {
                            Id = reader.GetInt32(0),
                            Name = reader.GetString(1),
                            TeacherName = reader.GetString(2),
                            CurrentCount = reader.GetInt32(3)
                        });
                    }

                    ViewBag.View = "groups";
                    return View(groups);
                }
            }
        }
        [HttpGet]
        public async Task<IActionResult> GetGroupDetails(int id)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                var model = new GroupDetailsViewModel();

                // Получаем название группы
                using (var cmd = new NpgsqlCommand("SELECT nameGroup FROM ChildrensGroup WHERE ID = @id", conn))
                {
                    cmd.Parameters.AddWithValue("id", id);
                    model.GroupName = (await cmd.ExecuteScalarAsync())?.ToString() ?? string.Empty;
                }

                // Получаем детей и их противопоказания
                using (var cmd = new NpgsqlCommand(@"
            SELECT 
                c.ID,
                c.fullname
            FROM Child c
            WHERE c.ID_group = @id 
            AND c.dateFinal IS NULL
            ORDER BY c.fullname", conn))
                {
                    cmd.Parameters.AddWithValue("id", id);

                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var child = new GroupChildViewModel
                        {
                            FullName = reader.GetString(1)
                        };
                        model.Children.Add(child);
                    }
                }

                // Для каждого ребенка получаем противопоказания и запрещенные продукты
                foreach (var child in model.Children)
                {
                    // Получаем противопоказания
                    using (var cmd = new NpgsqlCommand(@"
                SELECT DISTINCT con.name
                FROM Child c
                JOIN ContraindChildren cc ON c.ID = cc.ID_child
                JOIN Contraindications con ON cc.ID_contraind = con.ID
                WHERE c.ID_group = @groupId 
                AND c.dateFinal IS NULL 
                AND c.fullname = @fullname
                ORDER BY con.name", conn))
                    {
                        cmd.Parameters.AddWithValue("groupId", id);
                        cmd.Parameters.AddWithValue("fullname", child.FullName);

                        using var reader = await cmd.ExecuteReaderAsync();
                        while (await reader.ReadAsync())
                        {
                            child.Contraindications.Add(reader.GetString(0));
                        }
                    }

                    // Получаем запрещенные продукты
                    using (var cmd = new NpgsqlCommand(@"
                SELECT DISTINCT cp.name
                FROM Child c
                JOIN ContraindChildren cc ON c.ID = cc.ID_child
                JOIN ContraindWithProducts cwp ON cc.ID_contraind = cwp.ID_contraind
                JOIN ContraindicatedProducts cp ON cwp.ID_contraindProd = cp.ID
                WHERE c.ID_group = @groupId 
                AND c.dateFinal IS NULL 
                AND c.fullname = @fullname
                ORDER BY cp.name", conn))
                    {
                        cmd.Parameters.AddWithValue("groupId", id);
                        cmd.Parameters.AddWithValue("fullname", child.FullName);

                        using var reader = await cmd.ExecuteReaderAsync();
                        while (await reader.ReadAsync())
                        {
                            child.ForbiddenProducts.Add(reader.GetString(0));
                        }
                    }
                }

                return PartialView("_GroupDetails", model);
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetCurrentContraindications()
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using var cmd = new NpgsqlCommand(@"
            SELECT 
                c.fullname as child_name,
                cg.nameGroup as group_name,
                STRING_AGG(DISTINCT con.name, ', ') as contraindications
            FROM Child c
            JOIN ChildrensGroup cg ON c.ID_group = cg.ID
            JOIN ContraindChildren cc ON c.ID = cc.ID_child
            JOIN Contraindications con ON cc.ID_contraind = con.ID
            WHERE c.dateFinal IS NULL
            GROUP BY c.fullname, cg.nameGroup
            ORDER BY cg.nameGroup, c.fullname", conn);

                var result = new List<object>();
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    result.Add(new
                    {
                        childName = reader.GetString(0),
                        groupName = reader.GetString(1),
                        contraindications = reader.GetString(2).Split(", ").ToList()
                    });
                }

                return Json(result);
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetUniqueContraindications()
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                var model = new UniqueContraindicationsViewModel();

                // Получаем противопоказания
                using (var cmd = new NpgsqlCommand(@"
            SELECT DISTINCT con.name
            FROM Child c
            JOIN ContraindChildren cc ON c.ID = cc.ID_child
            JOIN Contraindications con ON cc.ID_contraind = con.ID
            WHERE c.dateFinal IS NULL
            ORDER BY con.name", conn))
                {
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        model.Contraindications.Add(reader.GetString(0));
                    }
                }

                // Получаем запрещенные продукты
                using (var cmd = new NpgsqlCommand(@"
            SELECT DISTINCT cp.name
            FROM Child c
            JOIN ContraindChildren cc ON c.ID = cc.ID_child
            JOIN ContraindWithProducts cwp ON cc.ID_contraind = cwp.ID_contraind
            JOIN ContraindicatedProducts cp ON cwp.ID_contraindProd = cp.ID
            WHERE c.dateFinal IS NULL
            ORDER BY cp.name", conn))
                {
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        model.ForbiddenProducts.Add(reader.GetString(0));
                    }
                }

                return PartialView("_UniqueContraindications", model);
            }
        }

        [HttpGet]
        public async Task<IActionResult> CreateInvoice()
        {
            if (HttpContext.Session.GetString("Role") != "cook")
                return RedirectToAction("Login", "Account");

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                var products = new List<ProductViewModel>();
                var invoices = new List<InvoiceViewModel>();

                // Сначала получаем продукты
                using (var cmd = new NpgsqlCommand(@"
            SELECT ID, nameProduct 
            FROM Products 
            ORDER BY nameProduct", conn))
                {
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        products.Add(new ProductViewModel
                        {
                            Id = reader.GetInt32(0),
                            Name = reader.GetString(1)
                        });
                    }
                }

                // Затем получаем накладные
                using (var cmd = new NpgsqlCommand(@"
            SELECT 
                i.ID,
                i.dateCreate as DateCreate,
                e.fullname as CookName,
                COALESCE(SUM(il.amount * il.cost), 0) as TotalAmount,
                i.statusInvoice as Status,
                i.ID_head,
                i.ID_receiver,
                i.dateAccept,
                i.dateReceipt
            FROM Invoice i
            JOIN Employee e ON i.ID_cook = e.ID
            LEFT JOIN InvoiceList il ON i.ID = il.ID_invoice
            GROUP BY i.ID, i.dateCreate, e.fullname, i.statusInvoice, i.ID_head, 
                     i.ID_receiver, i.dateAccept, i.dateReceipt
            ORDER BY i.dateCreate DESC", conn))
                {
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        invoices.Add(new InvoiceViewModel
                        {
                            Id = reader.GetInt32(0),
                            DateCreate = reader.GetDateTime(1),
                            CookName = reader.GetString(2),
                            TotalAmount = reader.GetDecimal(3),
                            Status = reader.GetString(4),
                            DateAccept = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                            DateReceipt = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                            Products = new List<InvoiceProductViewModel>()
                        });
                    }
                }

                ViewBag.Products = products;
                return View(invoices);
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreateInvoice(InvoiceViewModel model)
        {
            if (ModelState.IsValid)
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using var transaction = await conn.BeginTransactionAsync();
                    try
                    {
                        // Получаем ID текущего пользователя (повара)
                        var cookId = HttpContext.Session.GetInt32("UserId");

                        // Создаем новую накладную
                        using var cmd = new NpgsqlCommand(@"
                    INSERT INTO Invoice (ID_cook, dateCreate, statusInvoice) 
                    VALUES (@cookId, @dateCreate, @status) 
                    RETURNING ID", conn, transaction);

                        cmd.Parameters.AddWithValue("cookId", cookId);
                        cmd.Parameters.AddWithValue("dateCreate", model.DateCreate);
                        cmd.Parameters.AddWithValue("status", model.Status);

                        var invoiceId = (int)await cmd.ExecuteScalarAsync();

                        // Добавляем продукты в накладную
                        foreach (var product in model.Products.Where(p => p.Amount > 0))
                        {
                            using var cmdProduct = new NpgsqlCommand(@"
                        INSERT INTO InvoiceList (ID_invoice, ID_products, measure, amount) 
                        VALUES (@invoiceId, @productId, @measure, @amount)", conn, transaction);

                            cmdProduct.Parameters.AddWithValue("invoiceId", invoiceId);
                            cmdProduct.Parameters.AddWithValue("productId", product.ProductId);
                            cmdProduct.Parameters.AddWithValue("measure", product.Measure);
                            cmdProduct.Parameters.AddWithValue("amount", product.Amount);

                            await cmdProduct.ExecuteNonQueryAsync();
                        }

                        await transaction.CommitAsync();
                        return RedirectToAction("InvoiceDetails", new { id = invoiceId });
                    }
                    catch
                    {
                        await transaction.RollbackAsync();
                        throw;
                    }
                }
            }
            return View(model);
        }

        // Метод обновления отчета при выборе дат
        [HttpPost]
        public async Task<IActionResult> UpdateReport([FromBody] InvoiceViewModel model)
        {
            var reports = await GetReports(model.DateCreate, model.DateAccept);
            return Json(reports);
        }

        // Вспомогательный метод для получения данных
        private async Task<List<InvoiceExpenseReportViewModel>> GetReports(DateTime? startDate, DateTime? endDate)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                var query = @"
                SELECT 
                    i.ID as InvoiceId,
                    i.dateCreate as Date,
                    e.fullname as CookName,
                    COALESCE(SUM(il.amount * il.cost), 0) as TotalAmount,
                    i.statusInvoice as Status,
                    STRING_AGG(CONCAT(p.nameProduct, ': ', il.amount, ' ', il.measure), '; ') as ProductsList
                FROM Invoice i
                JOIN Employee e ON i.ID_cook = e.ID
                LEFT JOIN InvoiceList il ON i.ID = il.ID_invoice
                LEFT JOIN Products p ON il.ID_products = p.ID
                WHERE (CAST(@startDate AS timestamp) IS NULL OR i.dateCreate >= @startDate)
                AND (CAST(@endDate AS timestamp) IS NULL OR i.dateCreate <= @endDate)
                GROUP BY i.ID, i.dateCreate, e.fullname, i.statusInvoice
                ORDER BY i.dateCreate DESC";

                using var cmd = new NpgsqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@startDate", startDate.HasValue ? startDate.Value : DBNull.Value);
                cmd.Parameters.AddWithValue("@endDate", endDate.HasValue ? endDate.Value : DBNull.Value);

                var reports = new List<InvoiceExpenseReportViewModel>();
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    reports.Add(new InvoiceExpenseReportViewModel
                    {
                        InvoiceId = reader.GetInt32(0),
                        Date = reader.GetDateTime(1),
                        CookName = reader.GetString(2),
                        TotalAmount = reader.GetDecimal(3),
                        Status = reader.GetString(4),
                        ProductsList = !reader.IsDBNull(5) ? reader.GetString(5) : string.Empty
                    });
                }

                return reports;
            }
        }

        // Метод для получения деталей накладной
        [HttpGet]
        public async Task<IActionResult> GetInvoiceDetails(int id)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                var query = @"
                SELECT 
                    p.nameProduct,
                    il.measure,
                    il.amount,
                    il.cost,
                    (il.amount * il.cost) as total
                FROM InvoiceList il
                JOIN Products p ON il.ID_products = p.ID
                WHERE il.ID_invoice = @id";

                using var cmd = new NpgsqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@id", id);

                var products = new List<InvoiceProductViewModel>();
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    products.Add(new InvoiceProductViewModel
                    {
                        ProductName = reader.GetString(0),
                        Measure = reader.GetString(1),
                        Amount = reader.GetDecimal(2),
                        Cost = !reader.IsDBNull(3) ? reader.GetDecimal(3) : null
                    });
                }

                return PartialView("_InvoiceDetails", products);
            }
        }

        [HttpGet]
        public async Task<IActionResult> NewInvoice()
        {
            if (HttpContext.Session.GetString("Role") != "cook")
                return RedirectToAction("Login", "Account");

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                // Получаем список всех продуктов
                using var cmd = new NpgsqlCommand(@"
            SELECT ID, nameProduct 
            FROM Products 
            ORDER BY nameProduct", conn);

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

                ViewBag.Products = products;
                return View(new InvoiceViewModel());
            }
        }

        [HttpPost]
        public async Task<IActionResult> NewInvoice([FromBody] InvoiceViewModel model)
        {
            try
            {
                // Получаем имя пользователя из сессии
                var username = HttpContext.Session.GetString("Username");

                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using var transaction = await conn.BeginTransactionAsync();

                    try
                    {
                        // Получаем ID повара из базы данных по имени пользователя
                        using var cmdCook = new NpgsqlCommand(@"
                    SELECT ID 
                    FROM Employee 
                    WHERE fullname = @username", conn, transaction);

                        cmdCook.Parameters.AddWithValue("username", username);
                        var cookId = await cmdCook.ExecuteScalarAsync();
                        short cookIdShort = (short)cookId;

                        // Создаем накладную
                        using var cmdInvoice = new NpgsqlCommand(@"
                    INSERT INTO Invoice (ID_cook, statusInvoice) 
                    VALUES (@cookId, 'Перевіряється') 
                    RETURNING ID", conn, transaction);

                        cmdInvoice.Parameters.AddWithValue("cookId", cookIdShort);
                        var invoiceId = (int)await cmdInvoice.ExecuteScalarAsync();

                        // Добавляем продукты
                        foreach (var product in model.Products)
                        {
                            using var cmdProduct = new NpgsqlCommand(@"
                        INSERT INTO InvoiceList (ID_invoice, ID_products, measure, amount) 
                        VALUES (@invoiceId, @productId, @measure::measuretype, @amount)", conn, transaction);

                            cmdProduct.Parameters.AddWithValue("invoiceId", invoiceId);
                            cmdProduct.Parameters.AddWithValue("productId", product.ProductId);
                            cmdProduct.Parameters.AddWithValue("measure", product.Measure);
                            cmdProduct.Parameters.AddWithValue("amount", product.Amount);

                            await cmdProduct.ExecuteNonQueryAsync();
                        }

                        await transaction.CommitAsync();
                        return Json(new { success = true });
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        return Json(new { success = false, error = ex.Message });
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        // Метод для управления продуктами
        [HttpGet]
        public async Task<IActionResult> ManageProducts()
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using var cmd = new NpgsqlCommand("SELECT ID, nameProduct FROM Products ORDER BY nameProduct", conn);

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
            if (ModelState.IsValid)
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using var cmd = new NpgsqlCommand(
                        "INSERT INTO Products (nameProduct) VALUES (@name)", conn);
                    cmd.Parameters.AddWithValue("name", model.Name);
                    await cmd.ExecuteNonQueryAsync();
                }
                return RedirectToAction("ManageProducts");
            }
            return View("ManageProducts", model);
        }

        [HttpGet]
        public async Task<IActionResult> AcceptInvoice()
        {
            if (HttpContext.Session.GetString("Role") != "cook")
                return RedirectToAction("Login", "Account");

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                var query = @"
            SELECT 
                i.ID,
                i.dateCreate,
                ec.fullname as cook_name,
                eh.fullname as head_name,
                er.fullname as receiver_name,
                i.dateAccept,
                i.dateReceipt,
                i.statusInvoice,
                COALESCE(SUM(il.amount * il.cost), 0) as total_amount
            FROM Invoice i
            JOIN Employee ec ON i.ID_cook = ec.ID
            LEFT JOIN Employee eh ON i.ID_head = eh.ID
            LEFT JOIN Employee er ON i.ID_receiver = er.ID
            LEFT JOIN InvoiceList il ON i.ID = il.ID_invoice
            WHERE i.statusInvoice = 'Ухвалено'
            GROUP BY i.ID, i.dateCreate, ec.fullname, eh.fullname, er.fullname, 
                     i.dateAccept, i.dateReceipt, i.statusInvoice
            ORDER BY i.dateCreate DESC";

                using var cmd = new NpgsqlCommand(query, conn);
                var invoices = new List<InvoiceViewModel>();

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    invoices.Add(new InvoiceViewModel
                    {
                        Id = reader.GetInt32(0),
                        DateCreate = reader.GetDateTime(1),
                        CookName = reader.GetString(2),
                        HeadTeacherName = !reader.IsDBNull(3) ? reader.GetString(3) : null,
                        ReceiverName = !reader.IsDBNull(4) ? reader.GetString(4) : null,
                        DateAccept = !reader.IsDBNull(5) ? reader.GetDateTime(5) : null,
                        DateReceipt = !reader.IsDBNull(6) ? reader.GetDateTime(6) : null,
                        Status = reader.GetString(7),
                        TotalAmount = reader.GetDecimal(8),
                        Products = new List<InvoiceProductViewModel>()
                    });
                }

                return View(invoices);
            }
        }

        [HttpGet]
        public async Task<IActionResult> AcceptInvoiceDetails(int id)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                // Получаем основные данные накладной
                var query = @"
            SELECT 
                i.ID,
                i.dateCreate,
                ec.fullname as cook_name,
                eh.fullname as head_name,
                er.fullname as receiver_name,
                i.dateAccept,
                i.dateReceipt,
                i.statusInvoice
            FROM Invoice i
            JOIN Employee ec ON i.ID_cook = ec.ID
            LEFT JOIN Employee eh ON i.ID_head = eh.ID
            LEFT JOIN Employee er ON i.ID_receiver = er.ID
            WHERE i.ID = @id";

                using var cmd = new NpgsqlCommand(query, conn);
                cmd.Parameters.AddWithValue("id", id);

                var invoice = new InvoiceViewModel { Products = new List<InvoiceProductViewModel>() };

                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        invoice.Id = reader.GetInt32(0);
                        invoice.DateCreate = reader.GetDateTime(1);
                        invoice.CookName = reader.GetString(2);
                        invoice.HeadTeacherName = !reader.IsDBNull(3) ? reader.GetString(3) : null;
                        invoice.ReceiverName = !reader.IsDBNull(4) ? reader.GetString(4) : null;
                        invoice.DateAccept = !reader.IsDBNull(5) ? reader.GetDateTime(5) : null;
                        invoice.DateReceipt = !reader.IsDBNull(6) ? reader.GetDateTime(6) : null;
                        invoice.Status = reader.GetString(7);
                    }
                }

                // Получаем продукты
                var productsQuery = @"
            SELECT 
                p.ID,
                p.nameProduct,
                il.measure,
                il.amount,
                il.actualAmount,
                il.cost
            FROM InvoiceList il
            JOIN Products p ON il.ID_products = p.ID
            WHERE il.ID_invoice = @id";

                using (var cmdProducts = new NpgsqlCommand(productsQuery, conn))
                {
                    cmdProducts.Parameters.AddWithValue("id", id);
                    using var reader = await cmdProducts.ExecuteReaderAsync();
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

                return View(invoice);
            }
        }

        [HttpPost]
        public async Task<IActionResult> AcceptInvoice([FromBody] InvoiceViewModel model)
        {
            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using var transaction = await conn.BeginTransactionAsync();

                    try
                    {
                        var username = HttpContext.Session.GetString("Username");
                        using var cmdUser = new NpgsqlCommand(
                            "SELECT ID FROM Employee WHERE fullname = @username",
                            conn,
                            transaction);
                        cmdUser.Parameters.AddWithValue("username", username);
                        var receiverId = (short)await cmdUser.ExecuteScalarAsync();

                        // Обновляем накладную
                        using var cmdInvoice = new NpgsqlCommand(@"
                    UPDATE Invoice 
                    SET statusInvoice = 'Товар прийнятий',
                        ID_receiver = @receiverId,
                        dateReceipt = CURRENT_TIMESTAMP
                    WHERE ID = @invoiceId", conn, transaction);

                        cmdInvoice.Parameters.AddWithValue("receiverId", receiverId);
                        cmdInvoice.Parameters.AddWithValue("invoiceId", model.Id);
                        await cmdInvoice.ExecuteNonQueryAsync();

                        // Обновляем продукты
                        foreach (var product in model.Products)
                        {
                            using var cmdProduct = new NpgsqlCommand(@"
                        UPDATE InvoiceList 
                        SET actualAmount = @actualAmount,
                            cost = @cost
                        WHERE ID_invoice = @invoiceId 
                        AND ID_products = @productId", conn, transaction);

                            cmdProduct.Parameters.AddWithValue("actualAmount", product.ActualAmount ?? product.Amount);
                            cmdProduct.Parameters.AddWithValue("cost", product.Cost ?? 0);
                            cmdProduct.Parameters.AddWithValue("invoiceId", model.Id);
                            cmdProduct.Parameters.AddWithValue("productId", product.ProductId);
                            await cmdProduct.ExecuteNonQueryAsync();
                        }

                        await transaction.CommitAsync();
                        return Json(new { success = true });
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        return Json(new { success = false, error = ex.Message });
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }


        [HttpPost]
        public async Task<IActionResult> UpdateProduct([FromBody] ProductViewModel model)
        {
            if (string.IsNullOrEmpty(model.Name))
                return Json(new { success = false, message = "Назва продукту не може бути порожньою" });

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

                // Проверяем использование продукта в накладных
                using (var checkCmd = new NpgsqlCommand(
                    "SELECT EXISTS(SELECT 1 FROM InvoiceList WHERE ID_products = @id)", conn))
                {
                    checkCmd.Parameters.AddWithValue("id", id);
                    if ((bool)await checkCmd.ExecuteScalarAsync())
                    {
                        return Json(new
                        {
                            success = false,
                            message = "Неможливо видалити продукт, оскільки він використовується в накладних"
                        });
                    }
                }

                // Если продукт не используется, удаляем его
                using var cmd = new NpgsqlCommand("DELETE FROM Products WHERE id = @id", conn);
                cmd.Parameters.AddWithValue("id", id);
                await cmd.ExecuteNonQueryAsync();
                return Json(new { success = true });
            }
        }
    }
}