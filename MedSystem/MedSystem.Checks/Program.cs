using Microsoft.Data.Sqlite;
using System.Globalization;
using MedSystem.App.UI;
using MedSystem.Core;
using MedSystem.Core.Models;
using MedSystem.Core.Validation;
using MedSystem.Data;
using MedSystem.Data.Repositories;

if (args.Length == 2 && args[0] == "--verify-upgrade")
{
    Environment.ExitCode = UpgradeVerification.Run(args[1]);
    return;
}

var checks = 0;
void Check(bool condition, string scenario)
{
    if (!condition)
        throw new InvalidOperationException($"FAILED: {scenario}");
    checks++;
}

void ExpectFailure<T>(Action action, string scenario) where T : Exception
{
    try { action(); }
    catch (T) { checks++; return; }
    throw new InvalidOperationException($"FAILED: {scenario}");
}

var today = DateOnly.FromDateTime(DateTime.Now);
string Date(DateOnly value) => value.ToString(ExpirationRules.DateFormat, CultureInfo.InvariantCulture);
Check(ExpirationRules.GetMedicineStatus(Date(today.AddDays(-1))) == (true, false), "expired yesterday");
Check(ExpirationRules.GetMedicineStatus(Date(today)) == (false, true), "valid today");
Check(ExpirationRules.GetMedicineStatus(Date(today.AddDays(14))) == (false, true), "warning includes day 14");
Check(ExpirationRules.GetMedicineStatus(Date(today.AddDays(15))) == (false, false), "no warning on day 15");
Check(ExpirationRules.GetMedicineStatus("31.02.2026") == (false, false), "invalid date ignored");
Check(ExpirationRules.TryParseDate("29.02.2024", out _), "leap day accepted");
Check(!ExpirationRules.TryParseDate("29.02.2025", out _), "invalid leap day rejected");
Check(ExpirationRules.GetSingleCheckupStatus("29.02.2024", new DateOnly(2025, 2, 28)) == (false, true), "leap checkup valid until February 28");
Check(ExpirationRules.GetSingleCheckupStatus("29.02.2024", new DateOnly(2025, 3, 1)) == (true, false), "leap checkup expires on March 1");
Check(Pagination.ClampPage(99, 0, 100) == 1 && Pagination.ClampPage(-1, 300, 100) == 1, "pagination boundaries");
Check(ExpirationRules.GetPersonStatus(["", "invalid"]) == (false, false), "missing checkups ignored");
Check(Validators.ValidateStudent(new Student { LastName = "Иванов", FirstName = "Пётр" }).Count == 0,
    "optional student fields remain optional");
Check(Validators.ValidateMedicine(new Medicine { Name = "A", Dosage = "B", Quantity = -1, ExpirationDate = Date(today) }).Count == 1,
    "negative quantity rejected");

Appeal ValidAppeal() => new() { Number = 1, CreatedAt = Date(today), Sender = "Иванов Пётр", Complaints = "Боль" };
Check(Validators.ValidateAppeal(ValidAppeal()).Count == 0, "vital signs are optional");
var vitalAppeal = ValidAppeal();
vitalAppeal.HeightCm = 170;
vitalAppeal.WeightKg = 60.5;
vitalAppeal.Temperature = 36.6;
vitalAppeal.BloodPressureSystolic = 120;
vitalAppeal.BloodPressureDiastolic = 80;
vitalAppeal.Pulse = 72;
vitalAppeal.Saturation = 98;
vitalAppeal.CreatedTime = "09:05";
Check(Validators.ValidateAppeal(vitalAppeal).Count == 0, "normal vital signs accepted");
vitalAppeal.Temperature = 50;
vitalAppeal.Saturation = 101;
Check(Validators.ValidateAppeal(vitalAppeal).Count == 2, "out-of-range vital signs rejected");
var pressureAppeal = ValidAppeal();
pressureAppeal.BloodPressureSystolic = 120;
Check(Validators.ValidateAppeal(pressureAppeal).Count == 1, "blood pressure needs both values");
pressureAppeal.BloodPressureDiastolic = 130;
Check(Validators.ValidateAppeal(pressureAppeal).Count == 1, "systolic must exceed diastolic");
var timeAppeal = ValidAppeal();
timeAppeal.CreatedTime = "24:00";
Check(Validators.ValidateAppeal(timeAppeal).Count == 1 && Validators.IsValidTime("23:59"), "appeal time format");
Check(Validators.TryParseOptionalNumber("36,6", out var comma) && comma == 36.6
    && Validators.TryParseOptionalNumber(" 36.6 ", out var dot) && dot == 36.6
    && Validators.TryParseOptionalNumber("", out var empty) && empty is null
    && !Validators.TryParseOptionalNumber("36,6,1", out _)
    && !Validators.TryParseOptionalNumber("-1", out _), "decimal input accepts comma and dot");
Check(Validators.TryParseOptionalInteger("72", out var pulse) && pulse == 72
    && !Validators.TryParseOptionalInteger("72,5", out _), "integer input");
vitalAppeal.Temperature = 37.2;
vitalAppeal.Saturation = 98;
Check(vitalAppeal.VitalsSummary == "Рост 170 см · Вес 60,5 кг · Т 37,2 °C · АД 120/80 · Пульс 72 · SpO₂ 98%",
    "vitals summary order and Russian formatting");
Check(vitalAppeal.DateTimeDisplay == $"{Date(today)} 09:05" && ValidAppeal().DateTimeDisplay == Date(today),
    "date and time display");
Check(new Student { IsOnDispensaryRegister = true, DispensaryDetails = "у кардиолога" }.DispensaryDisplay == "Да — у кардиолога"
    && new Student().DispensaryDisplay == "Нет", "dispensary register display");

var loader = new LatestLoad();
var oldRead = new TaskCompletionSource();
var newRead = new TaskCompletionSource();
var loading = new List<bool>();
var applied = new List<int>();
var errors = new List<Exception>();
var oldLoad = loader.RunAsync(async token =>
{
    await oldRead.Task;
    token.ThrowIfCancellationRequested();
    applied.Add(1);
}, loading.Add, errors.Add);
var newLoad = loader.RunAsync(async token =>
{
    await newRead.Task;
    token.ThrowIfCancellationRequested();
    applied.Add(2);
}, loading.Add, errors.Add);
oldRead.SetResult();
await oldLoad;
Check(applied.Count == 0 && loading.SequenceEqual([true, true]), "obsolete result cannot clear current loading indicator");
newRead.SetResult();
await newLoad;
Check(applied.SequenceEqual([2]) && loading.SequenceEqual([true, true, false]), "latest result wins");
var delayed = loader.RunAsync(_ => { applied.Add(3); return Task.CompletedTask; }, loading.Add, errors.Add, 300);
loader.Cancel();
await delayed;
Check(applied.SequenceEqual([2]) && errors.Count == 0, "navigation cancels pending debounce without error");
await loader.RunAsync(_ => throw new InvalidOperationException("read failure"), loading.Add, errors.Add);
Check(errors.Count == 1 && !loading.Last(), "load errors are reported and indicator reset");

Exception? actionError = null;
Task ReportActionError(Exception ex)
{
    actionError = ex;
    return Task.CompletedTask;
}
await RecordAction.RunAsync(async () =>
{
    await Task.Yield();
    throw new InvalidOperationException("async action failure");
}, ReportActionError);
Check(actionError?.Message == "async action failure", "asynchronous record action failures are reported");

var directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), $"MedSystem.Checks-{Guid.NewGuid():N}"));
var originalDbPath = Db.DbPath;
Directory.CreateDirectory(directory);
Db.DbPath = Path.Combine(directory, "checks.db");
try
{
    DatabaseInitializer.Initialize();
    DatabaseInitializer.Initialize();
    Check(StudentRepository.GetPage(new()).Items.Count == 0, "empty students");
    Check(EmployeeRepository.GetPage(new()).Page == 1, "empty employees use page one");
    Check(MedicineRepository.GetPage(new()).TotalCount == 0, "empty medicines");
    Check(AppealRepository.GetPage(new()).TotalCount == 0, "empty appeals");

    Check(GroupRepository.Insert("11-ИС-А"), "insert group");
    var groupId = GroupRepository.GetAll().Single().Id;
    for (var i = 0; i < 3; i++)
        StudentRepository.Insert(new Student
        {
            LastName = $"Иванов{i}",
            FirstName = "Пётр",
            GroupId = i == 0 ? 0 : groupId,
            Oms = i == 0 ? "1234567890123456" : "",
            HealthGroup = "Основная",
        });
    var students = StudentRepository.GetPage(new() { Page = 999, PageSize = 2 });
    Check(students.Page == 2 && students.Items.Count == 1 && students.TotalCount == 3, "last student page clamped");
    Check(StudentRepository.GetPage(new() { Page = -3, PageSize = 0 }).Items.Count == 1, "page limits normalized");
    Check(StudentRepository.GetPage(new() { SearchText = " иВаНоВ " }).TotalCount == 3, "Cyrillic search ignores case");
    Check(StudentRepository.GetPage(new() { SearchText = "1234567890123456" }).TotalCount == 1, "OMS search");
    Check(StudentRepository.GetPage(new() { GroupId = -1 }).TotalCount == 1, "students without group");
    Check(StudentRepository.GetPage(new() { GroupSearchText = "ис-а" }).TotalCount == 2, "group name search");
    var student = StudentRepository.GetPage(new() { GroupId = -1 }).Items.Single();
    Check(StudentRepository.GetById(student.Id)?.HealthGroup == "Основная", "student mapping preserves health group");
    Check(StudentRepository.FindDuplicates(new Student { Oms = student.Oms }).Count == 1, "duplicate OMS detected");
    StudentRepository.MoveToTrash(student.Id);
    Check(StudentRepository.GetById(student.Id) is null, "trash excluded from lookup");
    Check(StudentRepository.FindDuplicates(new Student { Oms = student.Oms }).Single().Contains("в корзине"), "duplicates include trash");
    TrashRepository.Restore("student", student.Id);
    student.FluorographyDate = Date(today.AddYears(-2));
    student.MedicalExamDate = Date(today.AddDays(1).AddYears(-1));
    StudentRepository.Update(student);
    Check(ExpirationRules.GetPersonStatus([student.FluorographyDate, student.MedicalExamDate]) == (true, true), "expired and expiring checkups coexist");
    Check(StudentRepository.GetPage(new() { StatusFilter = 1 }).TotalCount == 1
        && StudentRepository.GetPage(new() { StatusFilter = 2 }).TotalCount == 1, "person SQL status agrees with core rules");

    Check(GroupRepository.ArchiveWithStudents(groupId, "Выпуск") == 2, "archive group and its students");
    Check(StudentRepository.GetPage(new() { Archived = true }).TotalCount == 2, "archived filter");
    Check(GroupRepository.RestoreFromArchive(groupId) == 2, "restore group and students together");
    Check(StudentRepository.GetPage(new()).TotalCount == 3, "active students restored");
    ExpectFailure<InvalidOperationException>(() => GroupRepository.MoveToTrash(groupId), "nonempty group protected");

    EmployeeRepository.Insert(new Employee { LastName = "Сидоров", FirstName = "Иван", PassportSeries = "1234", PassportNumber = "123456" });
    var employee = EmployeeRepository.GetPage(new() { SearchText = "СИДОРОВ" }).Items.Single();
    Check(employee.Affiliation == "", "nullable affiliation mapping");
    Check(EmployeeRepository.GetPage(new() { Page = 99 }).Page == 1, "employee page clamped");
    Check(EmployeeRepository.FindDuplicates(employee).Count == 0, "editing employee excludes itself");
    Check(EmployeeRepository.FindDuplicates(new Employee { PassportSeries = "1234", PassportNumber = "123456" }).Count == 1, "passport duplicate detected");
    EmployeeRepository.Archive(employee.Id, "Перевод");
    Check(EmployeeRepository.GetPage(new() { Archived = true }).TotalCount == 1, "employee archive");
    EmployeeRepository.RestoreFromArchive(employee.Id);

    MedicineRepository.Insert(new Medicine { Name = "Аспирин", Dosage = "100 мг", Quantity = 5, ExpirationDate = Date(today.AddDays(30)) });
    MedicineRepository.Insert(new Medicine { Name = "Бинт", Dosage = "1 шт", Quantity = 6, ExpirationDate = Date(today.AddDays(-1)) });
    MedicineRepository.Insert(new Medicine { Name = "Витамин", Dosage = "1 мг", Quantity = 6, ExpirationDate = Date(today.AddDays(14)) });
    Check(MedicineRepository.GetPage(new() { StatusFilter = 1 }).TotalCount == 1, "low quantity includes threshold");
    Check(MedicineRepository.GetPage(new() { StatusFilter = 2 }).TotalCount == 1, "expiring medicine filter");
    Check(MedicineRepository.GetPage(new() { StatusFilter = 3 }).TotalCount == 1, "expired medicine filter");
    Check(MedicineRepository.GetPage(new() { Page = 10, PageSize = 2 }).Page == 2, "medicine page clamped");
    Check(MedicineRepository.GetOrderCandidates(5).Count == 3 && MedicineRepository.HasOrderCandidates(5), "order candidates agree");
    var medicines = MedicineRepository.GetPage(new()).Items;
    ExpectFailure<InvalidOperationException>(() => MedicineRepository.Reorder(
    [
        (medicines[0].Id, new Medicine { Name = "Новая партия", Dosage = "1 мг", Quantity = 50, ExpirationDate = Date(today) }),
        (medicines[1].Id, new Medicine { Name = null! }),
    ]), "order transaction rolls back on invalid second item");
    Check(MedicineRepository.GetById(medicines[0].Id)?.Quantity == medicines[0].Quantity
        && MedicineRepository.GetPage(new()).TotalCount == 3, "failed order preserves old batches");

    AppealRepository.Insert(new Appeal { Number = 10, CreatedAt = Date(today), Sender = "Сидоров Иван", Complaints = "Боль" });
    var appeal = AppealRepository.GetPage(new() { SearchText = "сидоров", Page = 10 }).Items.Single();
    Check(appeal.Number == 10, "appeal search and page clamp");
    AppealRepository.MoveToTrash(appeal.Id);
    Check(AppealRepository.GetNextNumber() == 11, "number includes trashed appeals");
    Check(AppealRepository.SearchPersons("ПЁТР").Count == 3, "sender search includes active students");
    Check(IcdRepository.GetPage("орви", 99, 100).Items.Single().Code == "J06.9", "ICD search and page clamp");
    Check(IcdRepository.GetPage("missing", 99, 100).Page == 1, "empty ICD search");

    student.IsOnDispensaryRegister = true;
    student.DispensaryDetails = "у кардиолога";
    StudentRepository.Update(student);
    var registered = StudentRepository.GetById(student.Id);
    Check(registered is { IsOnDispensaryRegister: true, DispensaryDetails: "у кардиолога" }, "dispensary register saved");
    student.IsOnDispensaryRegister = false;
    StudentRepository.Update(student);
    Check(StudentRepository.GetById(student.Id) is { IsOnDispensaryRegister: false, DispensaryDetails: "" },
        "dispensary details cleared when register is off");

    var personOptions = AppealRepository.SearchPersons("ПЁТР");
    Check(personOptions.All(p => p.StudentId > 0) && personOptions.Any(p => p.StudentId == student.Id),
        "sender suggestions carry student ids");
    Check(AppealRepository.SearchPersons("Сидоров").Single().StudentId == 0, "employee suggestions are not linked");

    vitalAppeal.Number = 20;
    vitalAppeal.StudentId = student.Id;
    vitalAppeal.AmbulanceCalled = true;
    AppealRepository.Insert(vitalAppeal);
    AppealRepository.Insert(new Appeal
    {
        Number = 21,
        CreatedAt = Date(today.AddDays(-3)),
        CreatedTime = "23:00",
        StudentId = student.Id,
        Sender = student.FullName,
        Complaints = "Насморк",
    });
    AppealRepository.Insert(new Appeal { Number = 22, CreatedAt = Date(today), Sender = "Без привязки", Complaints = "Боль" });
    var history = AppealRepository.GetByStudent(student.Id);
    Check(history.Select(a => a.Number).SequenceEqual([20L, 21L]), "student history contains only linked appeals, newest date first");
    var savedAppeal = history[0];
    Check(savedAppeal is
    {
        CreatedTime: "09:05", HeightCm: 170, WeightKg: 60.5, Temperature: 37.2,
        BloodPressureSystolic: 120, BloodPressureDiastolic: 80, Pulse: 72, Saturation: 98, AmbulanceCalled: true,
    } && savedAppeal.StudentId == student.Id, "appeal vital signs round trip");
    savedAppeal.Temperature = null;
    savedAppeal.AmbulanceCalled = false;
    savedAppeal.StudentId = null;
    AppealRepository.Update(savedAppeal);
    Check(AppealRepository.GetById(savedAppeal.Id) is { Temperature: null, AmbulanceCalled: false, StudentId: null }
        && AppealRepository.GetByStudent(student.Id).Count == 1, "appeal update clears values and unlinks student");
    AppealRepository.MoveToTrash(history[1].Id);
    Check(AppealRepository.GetByStudent(student.Id).Count == 0, "trashed appeals hidden from student history");
    TrashRepository.Restore("appeal", history[1].Id);

    students = StudentRepository.GetPage(new() { Page = 2, PageSize = 2 });
    StudentRepository.MoveToTrash(students.Items.Single().Id);
    Check(StudentRepository.GetPage(new() { Page = 2, PageSize = 2 }).Page == 1, "deleting last row clamps page");
    Check(TrashRepository.GetPage(1, 100).TotalCount == 2, "trash contains deleted entities");
    TrashRepository.SetRetentionDays(0);
    Check(TrashRepository.PurgeExpired() == 0, "automatic trash cleanup can be disabled");

    StudentRepository.Insert(new Student { LastName = "Удаляемый", FirstName = "Студент" });
    var removable = StudentRepository.GetPage(new() { SearchText = "Удаляемый" }).Items.Single();
    AppealRepository.Insert(new Appeal
    {
        Number = 30,
        CreatedAt = Date(today),
        StudentId = removable.Id,
        Sender = removable.FullName,
        Complaints = "Кашель",
    });
    StudentRepository.MoveToTrash(removable.Id);
    TrashRepository.DeletePermanently("student", removable.Id);
    var orphan = AppealRepository.GetPage(new() { SearchText = "Удаляемый" }).Items.Single();
    Check(orphan.StudentId is null && orphan.Sender == removable.FullName,
        "permanently deleted student keeps appeals with sender text");

    var testDbPath = Db.DbPath;
    Db.DbPath = Path.Combine(directory, "missing", "checks.db");
    (string Name, Action Run)[] unavailableActions =
    [
        ("archive student", () => StudentRepository.Archive(student.Id, "Перевод")),
        ("restore student", () => StudentRepository.RestoreFromArchive(student.Id)),
        ("trash student", () => StudentRepository.MoveToTrash(student.Id)),
        ("archive employee", () => EmployeeRepository.Archive(employee.Id, "Перевод")),
        ("restore employee", () => EmployeeRepository.RestoreFromArchive(employee.Id)),
        ("trash employee", () => EmployeeRepository.MoveToTrash(employee.Id)),
        ("trash medicine", () => MedicineRepository.MoveToTrash(medicines[0].Id)),
        ("trash appeal", () => AppealRepository.MoveToTrash(appeal.Id)),
        ("check group before trash", () => GroupRepository.GetStudentCount(groupId)),
        ("trash group", () => GroupRepository.MoveToTrash(groupId)),
    ];
    foreach (var (name, run) in unavailableActions)
    {
        actionError = null;
        var refreshed = false;
        await RecordAction.RunAsync(() =>
        {
            run();
            refreshed = true;
            return Task.CompletedTask;
        }, ReportActionError);
        Check(actionError is SqliteException && !refreshed, $"{name}: database failure is reported without refreshing rows");
    }
    Db.DbPath = testDbPath;

    using (var connection = Db.Open())
    using (var command = connection.CreateCommand())
    {
        command.CommandText = """
            CREATE TRIGGER checks_reject_archive BEFORE UPDATE OF archived_at ON students
            BEGIN SELECT RAISE(ABORT, 'simulated write failure'); END
            """;
        command.ExecuteNonQuery();
    }
    actionError = null;
    var activeStudents = StudentRepository.GetPage(new()).TotalCount;
    var refreshCount = 0;
    Task ArchiveStudent()
    {
        StudentRepository.Archive(student.Id, "Перевод");
        refreshCount++;
        return Task.CompletedTask;
    }
    await RecordAction.RunAsync(ArchiveStudent, ReportActionError);
    Check(actionError is SqliteException && refreshCount == 0
        && StudentRepository.GetPage(new()).TotalCount == activeStudents, "failed write preserves student and visible list");
    using (var connection = Db.Open())
    using (var command = connection.CreateCommand())
    {
        command.CommandText = "DROP TRIGGER checks_reject_archive";
        command.ExecuteNonQuery();
    }
    actionError = null;
    await RecordAction.RunAsync(ArchiveStudent, ReportActionError);
    Check(actionError is null && refreshCount == 1
        && StudentRepository.GetPage(new() { Archived = true }).TotalCount == 1, "record action can be retried successfully after a failure");
    StudentRepository.RestoreFromArchive(student.Id);

    actionError = null;
    await RecordAction.RunAsync(() =>
    {
        GroupRepository.MoveToTrash(groupId);
        return Task.CompletedTask;
    }, ReportActionError);
    Check(actionError is InvalidOperationException && GroupRepository.GetStudentCount(groupId) > 0,
        "nonempty group error is reported and related students remain available");

    // ── Обновление базы старой версии (схема 0, как у python-версии) ─────
    var currentDbPath = Db.DbPath;
    var legacyDirectory = Path.Combine(directory, "legacy");
    Directory.CreateDirectory(legacyDirectory);
    Db.DbPath = Path.Combine(legacyDirectory, "med_system.db");
    using (var legacy = new SqliteConnection($"Data Source={Db.DbPath};Pooling=False"))
    {
        legacy.Open();
        using var command = legacy.CreateCommand();
        command.CommandText = """
            CREATE TABLE groups (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL UNIQUE);
            CREATE TABLE employees (
                id INTEGER PRIMARY KEY AUTOINCREMENT, last_name TEXT NOT NULL, first_name TEXT NOT NULL,
                middle_name TEXT NOT NULL, birth_date TEXT NOT NULL,
                affiliation TEXT CHECK(affiliation IN ('основной', 'внешний')),
                passport_series TEXT NOT NULL, passport_number TEXT NOT NULL, passport_issued_by TEXT NOT NULL,
                passport_issue_date TEXT NOT NULL, passport_department_code TEXT NOT NULL, oms TEXT NOT NULL,
                address TEXT NOT NULL, sanminimum_date TEXT NOT NULL, medical_exam_date TEXT NOT NULL,
                fluorography_date TEXT NOT NULL);
            CREATE TABLE students (
                id INTEGER PRIMARY KEY AUTOINCREMENT, group_id INTEGER, last_name TEXT NOT NULL,
                first_name TEXT NOT NULL, middle_name TEXT NOT NULL, birth_date TEXT NOT NULL, oms TEXT NOT NULL,
                address TEXT NOT NULL, sanminimum_date TEXT NOT NULL, medical_exam_date TEXT NOT NULL,
                fluorography_date TEXT NOT NULL, health_group TEXT NOT NULL DEFAULT '',
                FOREIGN KEY (group_id) REFERENCES groups(id) ON DELETE RESTRICT);
            CREATE TABLE medicines (
                id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, dosage TEXT NOT NULL,
                quantity INTEGER NOT NULL, expiration_date TEXT NOT NULL);
            CREATE TABLE appeals (
                id INTEGER PRIMARY KEY AUTOINCREMENT, number INTEGER NOT NULL, created_at TEXT NOT NULL,
                sender TEXT NOT NULL, birth_date TEXT NOT NULL, parent_phone TEXT NOT NULL,
                group_name TEXT NOT NULL, complaints TEXT NOT NULL, diagnosis TEXT NOT NULL,
                actions_recommendations TEXT NOT NULL);
            CREATE TABLE system_info (key TEXT PRIMARY KEY, value TEXT);
            CREATE TABLE icd_codes (code TEXT PRIMARY KEY, name TEXT NOT NULL);
            CREATE INDEX idx_students_group_id ON students(group_id);
            CREATE INDEX idx_appeals_number ON appeals(number);
            INSERT INTO groups (name) VALUES ('21-ИС-Б');
            INSERT INTO students (group_id, last_name, first_name, middle_name, birth_date, oms, address,
                                  sanminimum_date, medical_exam_date, fluorography_date, health_group)
            VALUES (1, 'Петрова', 'Анна', 'Сергеевна', '01.02.2007', '1111222233334444', 'ул. Ленина',
                    '01.09.2025', '02.09.2025', '03.09.2025', 'Основная'),
                   (1, 'Смирнов', 'Олег', '', '05.05.2006', '', '', '', '', '', ''),
                   (NULL, 'Смирнов', 'Олег', '', '05.05.2006', '', '', '', '', '', '');
            INSERT INTO employees (last_name, first_name, middle_name, birth_date, affiliation, passport_series,
                                   passport_number, passport_issued_by, passport_issue_date,
                                   passport_department_code, oms, address, sanminimum_date,
                                   medical_exam_date, fluorography_date)
            VALUES ('Кузнецова', 'Мария', '', '', 'основной', '', '', '', '', '', '', '', '', '', '');
            INSERT INTO students (group_id, last_name, first_name, middle_name, birth_date, oms, address,
                                  sanminimum_date, medical_exam_date, fluorography_date)
            VALUES (1, 'Кузнецова', 'Мария', '', '', '', '', '', '', '');
            INSERT INTO appeals (number, created_at, sender, birth_date, parent_phone, group_name,
                                 complaints, diagnosis, actions_recommendations)
            VALUES (1, '01.10.2025', 'петрова анна сергеевна', '01.02.2007', '', '21-ИС-Б', 'Боль', '', ''),
                   (2, '02.10.2025', 'Смирнов Олег', '05.05.2006', '', '', 'Кашель', '', ''),
                   (3, '03.10.2025', 'Кузнецова Мария', '', '', '', 'Головокружение', '', ''),
                   (4, '04.10.2025', 'Петрова Анна Сергеевна', '09.09.2009', '', '', 'Насморк', '', '');
            INSERT INTO system_info (key, value) VALUES ('last_group_increment_year', '2025');
            """;
        command.ExecuteNonQuery();
    }
    DatabaseInitializer.Initialize();
    DatabaseInitializer.Initialize();
    SqliteConnection.ClearAllPools();
    var upgradeBackups = Directory.GetFiles(BackupService.BackupDirectory, $"med_system_before_upgrade_v0_to_v{DatabaseInitializer.CurrentSchemaVersion}_*.db");
    Check(upgradeBackups.Length == 1, "one backup is created before upgrading an existing database");
    using (var backupConnection = new SqliteConnection($"Data Source={upgradeBackups[0]};Mode=ReadOnly;Pooling=False"))
    using (var command = backupConnection.CreateCommand())
    {
        backupConnection.Open();
        command.CommandText = "SELECT sanminimum_date FROM students WHERE last_name = 'Петрова'";
        Check((string?)command.ExecuteScalar() == "01.09.2025", "pre-upgrade backup keeps the original schema and data");
    }
    using (var upgraded = Db.Open())
    using (var command = upgraded.CreateCommand())
    {
        command.CommandText = "PRAGMA user_version";
        Check(Convert.ToInt32(command.ExecuteScalar()) == DatabaseInitializer.CurrentSchemaVersion, "legacy schema upgraded to current version");
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('students') WHERE name = 'sanminimum_date'";
        Check(Convert.ToInt32(command.ExecuteScalar()) == 0, "student sanminimum column removed");
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('employees') WHERE name = 'sanminimum_date'";
        Check(Convert.ToInt32(command.ExecuteScalar()) == 1, "employee sanminimum column kept");
        command.CommandText = "SELECT COUNT(*) FROM pragma_foreign_key_check";
        Check(Convert.ToInt32(command.ExecuteScalar()) == 0, "upgraded database has no foreign key violations");
        command.CommandText = "PRAGMA integrity_check";
        Check((string?)command.ExecuteScalar() == "ok", "upgraded database integrity");
        command.CommandText = "SELECT value FROM system_info WHERE key = 'last_group_increment_year'";
        Check((string?)command.ExecuteScalar() == "2025", "system settings preserved");
    }
    var upgradedStudent = StudentRepository.GetPage(new() { SearchText = "Петрова" }).Items.Single();
    Check(upgradedStudent is
    {
        BirthDate: "01.02.2007", Oms: "1111222233334444", Address: "ул. Ленина", MedicalExamDate: "02.09.2025",
        FluorographyDate: "03.09.2025", HealthGroup: "Основная", GroupName: "21-ИС-Б", IsOnDispensaryRegister: false,
    }, "student data preserved by upgrade");
    Check(StudentRepository.GetPage(new()).TotalCount == 4 && GroupRepository.GetAll().Count == 1
        && EmployeeRepository.GetPage(new()).TotalCount == 1, "row counts preserved by upgrade");
    var upgradedAppeals = AppealRepository.GetPage(new()).Items.ToDictionary(a => a.Number);
    Check(upgradedAppeals.Count == 4 && upgradedAppeals[1].StudentId == upgradedStudent.Id
        && upgradedAppeals[1].Complaints == "Боль" && upgradedAppeals[1].CreatedTime == ""
        && upgradedAppeals[1].Temperature is null && !upgradedAppeals[1].AmbulanceCalled,
        "unique legacy appeal linked to its student, new fields empty");
    Check(upgradedAppeals[2].StudentId is null, "ambiguous legacy appeal stays unlinked");
    Check(upgradedAppeals[3].StudentId is null, "legacy appeal matching an employee stays unlinked");
    Check(upgradedAppeals[4].StudentId is null, "legacy appeal with another birth date stays unlinked");
    Check(StudentRepository.GetPage(new() { StatusFilter = 1 }).TotalCount == 1, "student status works after upgrade");
    SqliteConnection.ClearAllPools();
    Db.DbPath = currentDbPath;

    var backup = BackupService.CreateManualBackup(Path.Combine(directory, "backup.db"));
    BackupService.ValidateBackup(backup);
    Check(File.Exists(backup), "backup validates");
    ExpectFailure<InvalidOperationException>(() => BackupService.CreateManualBackup(Db.DbPath), "backup cannot overwrite live database");
    Db.DbPath = Path.Combine(directory, "features.db");
    DatabaseInitializer.Initialize();
    var beforeBoundary = new DateTime(2026, 8, 14);
    Check(GroupRepository.Insert("31 ИС") && GroupRepository.Insert("41 ИС")
        && GroupRepository.Insert("Курсы"), "graduation fixture");
    var graduate = GroupRepository.GetAll().Single(g => g.Name == "41 ИС");
    StudentRepository.Insert(new Student { GroupId = graduate.Id, LastName = "Выпускник", FirstName = "Иван" });
    Check(GroupRepository.AdvanceAcademicYear(beforeBoundary) == 0, "day before rollover");
    Check(GroupRepository.AdvanceAcademicYear(beforeBoundary.AddDays(1)) == 2, "promote and graduate on August 15");
    Check(GroupRepository.GetAll().Any(g => g.Name == "41 ИС" && g.Id != graduate.Id)
        && GroupRepository.GetAll(archived: true).Single().Id == graduate.Id
        && StudentRepository.GetPage(new() { Archived = true }).Items.Single().GroupId == graduate.Id,
        "reuse graduate name, keep archive identity and student");
    Check(GroupRepository.AdvanceAcademicYear(beforeBoundary.AddDays(2), manual: true) == 0,
        "manual annual action cannot repeat automatic graduation");
    Check(GroupRepository.AdvanceAcademicYear(new DateTime(2028, 1, 10)) == 1
        && GroupRepository.GetAll(archived: true).Count == 2, "catch up missed rollover in January");

    var icd = IcdImport.Read(new StringReader("Код;Наименование\nJ06.9;Старая запись\nz99.1;\"Пример; с разделителем\"\nZ99.1;\"Пример; с разделителем\""));
    Check(icd.Count == 2 && IcdRepository.Import(icd) == 1 && IcdRepository.Import(icd) == 0
        && IcdRepository.Search("J06.9").Single().Name != "Старая запись", "CSV import preserves edited codes and is idempotent");
    ExpectFailure<InvalidDataException>(() => IcdImport.Read(new StringReader("A00,x\na00,y")), "conflicting names in CSV");
    ExpectFailure<InvalidDataException>(() => IcdImport.Read(new StringReader("A00\n")), "reject missing ICD name");

    checks += AppealFeatureChecks.Run();
    StudentImportChecks.Run();
    checks++;
    MedicalChecks.Run();
    checks++;
    Console.WriteLine($"Passed {checks} regression checks.");
}
finally
{
    Db.DbPath = originalDbPath;
    SqliteConnection.ClearAllPools();
    // Only remove this run's explicitly created temporary directory.
    if (Path.GetDirectoryName(directory) == Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()))
        && Path.GetFileName(directory).StartsWith("MedSystem.Checks-", StringComparison.Ordinal))
        Directory.Delete(directory, recursive: true);
}
