using Diana_Litvinova_IPZ_4_0_1.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

namespace Diana_Litvinova_IPZ_4_0_1.Controllers
{
    public class TeacherController : Controller
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private string _connectionString => _httpContextAccessor.HttpContext?.Session.GetString("ConnectionString");

        public TeacherController(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public IActionResult Index()
        {
            if (HttpContext.Session.GetString("Role") != "teacher")
                return RedirectToAction("Login", "Account");
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> GroupManagement(int? groupId = null)
        {
            if (HttpContext.Session.GetString("Role") != "teacher")
                return RedirectToAction("Login", "Account");

            var teacherName = HttpContext.Session.GetString("Username");
            var groups = new List<GroupViewModel>();

            // Получаем список всех групп преподавателя
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                var groupsQuery = @"
                SELECT 
                    cg.ID,
                    cg.nameGroup,
                    COUNT(c.ID) as current_count,
                    cg.capacity
                FROM ChildrensGroup cg
                LEFT JOIN Child c ON cg.ID = c.ID_group AND c.dateFinal IS NULL
                JOIN Employee e ON cg.ID_teacher = e.ID
                WHERE e.fullname = @teacherName
                GROUP BY cg.ID, cg.nameGroup, cg.capacity";

                using var cmd = new NpgsqlCommand(groupsQuery, conn);
                cmd.Parameters.AddWithValue("teacherName", teacherName);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    groups.Add(new GroupViewModel
                    {
                        Id = reader.GetInt32(0),
                        Name = reader.GetString(1),
                        CurrentCount = reader.GetInt32(2),
                        Capacity = reader.GetInt32(3)
                    });
                }
            }

            if (!groups.Any())
                return View("NoGroup");

            // Если groupId не указан или указан неверно, берем первую группу
            if (!groupId.HasValue || !groups.Any(g => g.Id == groupId.Value))
                groupId = groups.First().Id;

            // Получаем детальную информацию о выбранной группе
            var selectedGroup = await GetGroupDetails(groupId.Value);

            if (selectedGroup == null)
                return View("NoGroup");

            ViewBag.TeacherGroups = groups;
            return View(selectedGroup);
        }

        private async Task<GroupViewModel> GetGroupDetails(int groupId)
        {
            var group = new GroupViewModel();

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                // Получаем основную информацию о группе
                var query = @"
                WITH GroupAttendance AS (
                    SELECT 
                        c.ID_group,
                        COUNT(DISTINCT c.ID) as total_children,
                        COUNT(DISTINCT CASE WHEN t.statusVisit = 'Присутній' THEN c.ID END) as present_children
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
                    e.ID as teacher_id,
                    cg.capacity,
                    ga.total_children as current_count,
                    CASE 
                        WHEN ga.total_children > 0 
                        THEN (ga.present_children * 100.0 / ga.total_children)
                        ELSE 0 
                    END as attendance_percentage
                FROM ChildrensGroup cg
                JOIN Employee e ON cg.ID_teacher = e.ID
                LEFT JOIN GroupAttendance ga ON cg.ID = ga.ID_group
                WHERE cg.ID = @groupId";

                using var cmd = new NpgsqlCommand(query, conn);
                cmd.Parameters.AddWithValue("groupId", groupId);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    group.Id = reader.GetInt32(0);
                    group.Name = reader.GetString(1);
                    group.TeacherName = reader.GetString(2);
                    group.TeacherId = reader.GetInt32(3);
                    group.Capacity = reader.GetInt32(4);
                    group.CurrentCount = reader.GetInt32(5);
                    group.AttendancePercentage = reader.GetDecimal(6);
                    group.Children = new List<ChildInGroupViewModel>();
                }
                else
                {
                    return null;
                }
            }

            // Получаем информацию о детях в группе
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                var childQuery = @"
                SELECT 
                    c.ID,
                    c.fullname,
                    f.fullnameResponsible as family_name,
                    c.dateStart,
                    c.dateFinal,
                    STRING_AGG(DISTINCT con.name, ', ') as contraindications,
                    t.statusVisit
                FROM Child c
                JOIN Family f ON c.ID_family = f.ID
                LEFT JOIN ContraindChildren cc ON c.ID = cc.ID_child
                LEFT JOIN Contraindications con ON cc.ID_contraind = con.ID
                LEFT JOIN Tabulation t ON c.ID = t.ID_child 
                    AND t.dateVisit = CURRENT_DATE
                WHERE c.ID_group = @groupId AND c.dateFinal IS NULL
                GROUP BY c.ID, c.fullname, f.fullnameResponsible, c.dateStart, 
                         c.dateFinal, t.statusVisit
                ORDER BY c.fullname";

                using var cmd = new NpgsqlCommand(childQuery, conn);
                cmd.Parameters.AddWithValue("groupId", groupId);

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
                        AttendanceStatus = !reader.IsDBNull(6) ? reader.GetString(6) : "Відсутній"
                    });
                }
            }

            return group;
        }

        [HttpGet]
        public async Task<IActionResult> ManageAttendance(int? groupId = null)
        {
            if (HttpContext.Session.GetString("Role") != "teacher")
                return RedirectToAction("Login", "Account");

            // Если groupId не указан, получаем ID первой группы учителя
            if (!groupId.HasValue)
            {
                var teacherName = HttpContext.Session.GetString("Username");
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using var cmd = new NpgsqlCommand(@"
                    SELECT cg.ID 
                    FROM ChildrensGroup cg
                    JOIN Employee e ON cg.ID_teacher = e.ID
                    WHERE e.fullname = @teacherName
                    LIMIT 1", conn);
                    cmd.Parameters.AddWithValue("teacherName", teacherName);
                    var result = await cmd.ExecuteScalarAsync();
                    if (result != null)
                        groupId = (int)result;
                    else
                        return RedirectToAction("NoGroup");
                }
            }

            return View(await GetGroupDetails(groupId.Value));
        }

        [HttpPost]
        public async Task<IActionResult> UpdateAttendance([FromBody] List<AttendanceUpdateModel> updates)
        {
            if (updates == null || !updates.Any())
                return Json(new { success = false, message = "Немає даних для оновлення" });

            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    foreach (var update in updates)
                    {
                        // Сначала проверяем существует ли запись
                        var checkQuery = "SELECT id FROM Tabulation WHERE ID_child = @childId AND dateVisit = CURRENT_DATE";
                        int? existingId = null;

                        using (var checkCmd = new NpgsqlCommand(checkQuery, conn))
                        {
                            checkCmd.Parameters.AddWithValue("childId", update.ChildId);
                            var result = await checkCmd.ExecuteScalarAsync();
                            if (result != null && result != DBNull.Value)
                            {
                                existingId = Convert.ToInt32(result);
                            }
                        }

                        if (existingId.HasValue)
                        {
                            // Обновляем существующую запись
                            var updateQuery = "UPDATE Tabulation SET statusVisit = @status::statusvisit WHERE id = @id";
                            using (var updateCmd = new NpgsqlCommand(updateQuery, conn))
                            {
                                updateCmd.Parameters.AddWithValue("status", update.Status);
                                updateCmd.Parameters.AddWithValue("id", existingId.Value);
                                await updateCmd.ExecuteNonQueryAsync();
                            }
                        }
                        else
                        {
                            // Создаем новую запись
                            var insertQuery = @"
                        INSERT INTO Tabulation (ID_child, dateVisit, statusVisit)
                        VALUES (@childId, CURRENT_DATE, @status::statusvisit)";

                            using (var insertCmd = new NpgsqlCommand(insertQuery, conn))
                            {
                                insertCmd.Parameters.AddWithValue("childId", update.ChildId);
                                insertCmd.Parameters.AddWithValue("status", update.Status);
                                await insertCmd.ExecuteNonQueryAsync();
                            }
                        }
                    }
                }

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }


        [HttpGet]
        public async Task<IActionResult> Families(string searchTerm = "", int? groupId = null)
        {
            if (HttpContext.Session.GetString("Role") != "teacher")
                return RedirectToAction("Login", "Account");

            var teacherName = HttpContext.Session.GetString("Username");
            var model = new FamilySearchViewModel
            {
                SearchTerm = searchTerm,
                SelectedGroupId = groupId,
                Children = new List<ChildWithFamilyViewModel>(),
                Groups = new List<GroupViewModel>()
            };

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                // Получаем список ВСЕХ групп с пометкой принадлежности текущему учителю
                var groupsQuery = @"
            SELECT 
                cg.ID, 
                cg.nameGroup,
                e.fullname as teacherName,
                e.ID as teacherId,
                cg.capacity,
                COUNT(c.ID) as currentCount,
                COALESCE(pc.cost, 0) as currentPrice,
                e.fullname = @teacherName as isTeacherGroup,
                COALESCE(AVG(CASE WHEN t.statusVisit = 'Присутній' THEN 100 ELSE 0 END), 0) as attendancePercentage
            FROM ChildrensGroup cg
            JOIN Employee e ON cg.ID_teacher = e.ID
            LEFT JOIN Child c ON cg.ID = c.ID_group AND c.dateFinal IS NULL
            LEFT JOIN PriceChronology pc ON cg.ID = pc.ID_group AND pc.dateFinal IS NULL
            LEFT JOIN Tabulation t ON c.ID = t.ID_child AND t.dateVisit = CURRENT_DATE
            GROUP BY cg.ID, cg.nameGroup, e.fullname, e.ID, cg.capacity, pc.cost
            ORDER BY 
                e.fullname = @teacherName DESC, 
                cg.nameGroup ASC";

                using (var cmd = new NpgsqlCommand(groupsQuery, conn))
                {
                    cmd.Parameters.AddWithValue("teacherName", teacherName);
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
                            CurrentPrice = reader.GetDecimal(6),
                            IsTeacherGroup = reader.GetBoolean(7),
                            AttendancePercentage = reader.GetDecimal(8)
                        });
                    }
                }

                // Если группа не выбрана, берем первую группу текущего учителя
                if (!groupId.HasValue && model.Groups.Any(g => g.IsTeacherGroup))
                {
                    groupId = model.Groups.First(g => g.IsTeacherGroup).Id;
                    model.SelectedGroupId = groupId;
                }

                // Получаем детей и информацию о семьях
                var childrenQuery = @"
            SELECT 
                c.ID,
                c.fullname as childName,
                cg.nameGroup,
                e.fullname as teacherName,
                f.fullnameResponsible,
                f.phoneResponsible,
                c.dateStart,
                STRING_AGG(DISTINCT con.name, ', ') as contraindications
            FROM Child c
            JOIN ChildrensGroup cg ON c.ID_group = cg.ID
            JOIN Employee e ON cg.ID_teacher = e.ID
            JOIN Family f ON c.ID_family = f.ID
            LEFT JOIN ContraindChildren cc ON c.ID = cc.ID_child
            LEFT JOIN Contraindications con ON cc.ID_contraind = con.ID
            WHERE (@groupId IS NULL OR c.ID_group = @groupId)
            AND c.dateFinal IS NULL
            AND (@searchTerm = '' 
                OR f.fullnameResponsible ILIKE '%' || @searchTerm || '%'
                OR f.phoneResponsible LIKE '%' || @searchTerm || '%'
                OR c.fullname ILIKE '%' || @searchTerm || '%')
            GROUP BY 
                c.ID, c.fullname, cg.nameGroup, e.fullname,
                f.fullnameResponsible, f.phoneResponsible, c.dateStart
            ORDER BY c.fullname";

                using (var cmd = new NpgsqlCommand(childrenQuery, conn))
                {
                    cmd.Parameters.AddWithValue("groupId", (object)groupId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("searchTerm", searchTerm ?? "");

                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        model.Children.Add(new ChildWithFamilyViewModel
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
                        });
                    }
                }
            }

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> FamilyDetails(int childId)
        {
            if (HttpContext.Session.GetString("Role") != "teacher")
                return RedirectToAction("Login", "Account");

            var family = new FamilyViewModel();

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                // Получаем основную информацию о семье
                var familyQuery = @"
            SELECT 
                f.ID,
                f.fullnameResponsible,
                f.phoneResponsible,
                f.placeOfResidence
            FROM Family f
            JOIN Child c ON f.ID = c.ID_family
            WHERE c.ID = @childId";

                using (var cmd = new NpgsqlCommand(familyQuery, conn))
                {
                    cmd.Parameters.AddWithValue("childId", childId);
                    using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        family.Id = reader.GetInt32(0);
                        family.ResponsibleName = reader.GetString(1);
                        family.PhoneResponsible = reader.GetString(2);
                        family.PlaceOfResidence = reader.GetString(3);
                    }
                }

                // Получаем дополнительных ответственных лиц
                var responsiblesQuery = @"
            SELECT ID, fullname, phone
            FROM Responsible
            WHERE ID_family = @familyId
            ORDER BY fullname";

                using (var cmd = new NpgsqlCommand(responsiblesQuery, conn))
                {
                    cmd.Parameters.AddWithValue("familyId", family.Id);
                    using var reader = await cmd.ExecuteReaderAsync();
                    family.AdditionalResponsibles = new List<ResponsibleViewModel>();
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

                // Получаем всех детей в семье
                var childrenQuery = @"
            SELECT 
                c.ID,
                c.fullname,
                cg.nameGroup,
                c.dateStart,
                c.dateFinal,
                STRING_AGG(DISTINCT con.name, ', ') as contraindications
            FROM Child c
            JOIN ChildrensGroup cg ON c.ID_group = cg.ID
            LEFT JOIN ContraindChildren cc ON c.ID = cc.ID_child
            LEFT JOIN Contraindications con ON cc.ID_contraind = con.ID
            WHERE c.ID_family = @familyId AND c.dateFinal IS NULL
            GROUP BY c.ID, c.fullname, cg.nameGroup, c.dateStart, c.dateFinal
            ORDER BY c.fullname";

                using (var cmd = new NpgsqlCommand(childrenQuery, conn))
                {
                    cmd.Parameters.AddWithValue("familyId", family.Id);
                    using var reader = await cmd.ExecuteReaderAsync();
                    family.Children = new List<ChildViewModel>();
                    while (await reader.ReadAsync())
                    {
                        family.Children.Add(new ChildViewModel
                        {
                            Id = reader.GetInt32(0),
                            FullName = reader.GetString(1),
                            GroupName = reader.GetString(2),
                            StartDate = reader.GetDateTime(3),
                            EndDate = !reader.IsDBNull(4) ? reader.GetDateTime(4) : null,
                            Contraindications = !reader.IsDBNull(5)
                                ? reader.GetString(5).Split(", ").ToList()
                                : new List<string>()
                        });
                    }
                }
            }

            return View(family);
        }

        [HttpGet]
        public async Task<IActionResult> Attendance(int? groupId = null, string period = "day", DateTime? startDate = null, DateTime? endDate = null)
        {
            if (HttpContext.Session.GetString("Role") != "teacher")
                return RedirectToAction("Login", "Account");

            var teacherName = HttpContext.Session.GetString("Username");
            var model = new AttendanceViewModel
            {
                TeacherGroups = new List<GroupViewModel>(),
                Period = period,
                StartDate = startDate ?? DateTime.Now.AddMonths(-1),
                EndDate = endDate ?? DateTime.Now,
                SelectedGroupId = groupId
            };

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                // Получаем группы учителя
                var groupsQuery = @"
            SELECT cg.ID, cg.nameGroup
            FROM ChildrensGroup cg
            JOIN Employee e ON cg.ID_teacher = e.ID
            WHERE e.fullname = @teacherName";

                using (var cmd = new NpgsqlCommand(groupsQuery, conn))
                {
                    cmd.Parameters.AddWithValue("teacherName", teacherName);
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        model.TeacherGroups.Add(new GroupViewModel
                        {
                            Id = reader.GetInt32(0),
                            Name = reader.GetString(1)
                        });
                    }
                }

                if (!model.SelectedGroupId.HasValue && model.TeacherGroups.Any())
                {
                    model.SelectedGroupId = model.TeacherGroups.First().Id;
                }

                if (model.SelectedGroupId.HasValue)
                {
                    model.StatsData = await GetAttendanceStats(conn, model.SelectedGroupId.Value,
                        model.StartDate, model.EndDate, period);
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
        public async Task<IActionResult> GetAttendanceData(int groupId, string period, DateTime startDate, DateTime endDate)
        {
            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            var stats = await GetAttendanceStats(conn, groupId, startDate, endDate, period);
            return Json(stats);
        }

        public async Task<IActionResult> Contraindications()
        {
            if (HttpContext.Session.GetString("Role") != "teacher")
                return RedirectToAction("Login", "Account");

            var teacherName = HttpContext.Session.GetString("Username");
            var model = new TeacherContraindicationsViewModel();

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                // Получаем группы учителя с детьми и их противопоказаниями
                var query = @"
            WITH TeacherGroups AS (
                SELECT cg.ID, cg.nameGroup
                FROM ChildrensGroup cg
                JOIN Employee e ON cg.ID_teacher = e.ID
                WHERE e.fullname = @teacherName
            )
            SELECT 
                tg.ID as GroupId,
                tg.nameGroup as GroupName,
                c.ID as ChildId,
                c.fullname as ChildName,
                STRING_AGG(DISTINCT con.ID::text || '|' || con.name, ';') as Contraindications,
                STRING_AGG(DISTINCT cp.ID::text || '|' || cp.name, ';') as ForbiddenProducts
            FROM TeacherGroups tg
            JOIN Child c ON tg.ID = c.ID_group
            LEFT JOIN ContraindChildren cc ON c.ID = cc.ID_child
            LEFT JOIN Contraindications con ON cc.ID_contraind = con.ID
            LEFT JOIN ContraindWithProducts cwp ON con.ID = cwp.ID_contraind
            LEFT JOIN ContraindicatedProducts cp ON cwp.ID_contraindProd = cp.ID
            WHERE c.dateFinal IS NULL
            GROUP BY tg.ID, tg.nameGroup, c.ID, c.fullname
            ORDER BY tg.nameGroup, c.fullname";

                using (var cmd = new NpgsqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("teacherName", teacherName);
                    using var reader = await cmd.ExecuteReaderAsync();

                    ContraindicationGroupViewModel currentGroup = null;

                    while (await reader.ReadAsync())
                    {
                        var groupId = reader.GetInt32(0);

                        if (currentGroup == null || currentGroup.GroupId != groupId)
                        {
                            currentGroup = new ContraindicationGroupViewModel
                            {
                                GroupId = groupId,
                                GroupName = reader.GetString(1)
                            };
                            model.Groups.Add(currentGroup);
                        }

                        var childContraindications = new ChildContraindicationViewModel
                        {
                            ChildId = reader.GetInt32(2),
                            ChildName = reader.GetString(3)
                        };

                        // Обработка противопоказаний
                        if (!reader.IsDBNull(4))
                        {
                            foreach (var contra in reader.GetString(4).Split(';'))
                            {
                                var parts = contra.Split('|');
                                childContraindications.Contraindications.Add(new ContraindicationViewModel
                                {
                                    Id = int.Parse(parts[0]),
                                    Name = parts[1]
                                });
                            }
                        }

                        // Обработка запрещенных продуктов
                        if (!reader.IsDBNull(5))
                        {
                            foreach (var product in reader.GetString(5).Split(';'))
                            {
                                var parts = product.Split('|');
                                childContraindications.ForbiddenProducts.Add(new ContraindicatedProductViewModel
                                {
                                    Id = int.Parse(parts[0]),
                                    Name = parts[1]
                                });
                            }
                        }

                        currentGroup.Children.Add(childContraindications);
                    }
                }

                // Получаем список всех продуктов
                using (var cmd = new NpgsqlCommand(
                    "SELECT ID, name FROM ContraindicatedProducts ORDER BY name", conn))
                {
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        model.AllProducts.Add(new ContraindicatedProductViewModel
                        {
                            Id = reader.GetInt32(0),
                            Name = reader.GetString(1)
                        });
                    }
                }
            }

            return View(model);
        }
    }
}
