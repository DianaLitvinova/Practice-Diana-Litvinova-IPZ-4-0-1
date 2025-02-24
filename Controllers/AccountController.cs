using Diana_Litvinova_IPZ_4_0_1.Models;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace Diana_Litvinova_IPZ_4_0_1.Controllers
{
    public class AccountController : Controller
    {
        private readonly IConfiguration _configuration;

        public AccountController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            try
            {
                var userConnString = $"Host=localhost;Database=KinderGarden;Username={model.Username};Password={model.Password}";
                using (var conn = new NpgsqlConnection(userConnString))
                {
                    await conn.OpenAsync();

                    HttpContext.Session.SetString("Username", model.Username);
                    HttpContext.Session.SetString("ConnectionString", userConnString);

                    // Определяем роль и перенаправляем
                    var role = DetermineUserRole(conn, model.Username);
                    HttpContext.Session.SetString("Role", role);

                    // Перенаправляем на нужную страницу в зависимости от роли
                    switch (role)
                    {
                        case "head_teacher":
                            return RedirectToAction("Index", "HeadTeacher");
                        case "teacher":
                            return RedirectToAction("Index", "Teacher");
                        case "cook":
                            return RedirectToAction("Index", "Cook");
                        default:
                            return RedirectToAction("Login", "Account");
                    }
                }
            }
            catch
            {
                ModelState.AddModelError("", "Неверное имя пользователя или пароль");
                return View(model);
            }
        }

        private string DetermineUserRole(NpgsqlConnection conn, string username)
        {
            try
            {
                using (var cmd = new NpgsqlCommand(@"
            SELECT DISTINCT r.rolname 
            FROM pg_roles r 
            JOIN pg_auth_members m ON r.oid = m.roleid
            JOIN pg_roles u ON m.member = u.oid
            WHERE u.rolname = @username 
            AND NOT r.rolcanlogin", conn))
                {
                    cmd.Parameters.AddWithValue("username", username);
                    var role = cmd.ExecuteScalar()?.ToString();
                    Console.WriteLine($"Determined role: {role}"); // Логирование роли
                    return role ?? "user";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error determining role: {ex.Message}"); // Логирование ошибки
                return "user";
            }
        }

        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }
    }
}