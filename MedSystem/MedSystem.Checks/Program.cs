using Microsoft.Data.Sqlite;
using System.Globalization;
using MedSystem.App.UI;
using MedSystem.Core;
using MedSystem.Core.Models;
using MedSystem.Core.Validation;
using MedSystem.Data;
using MedSystem.Data.Repositories;

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
    student.SanminimumDate = Date(today.AddYears(-2));
    student.MedicalExamDate = Date(today.AddDays(1).AddYears(-1));
    StudentRepository.Update(student);
    Check(ExpirationRules.GetPersonStatus([student.SanminimumDate, student.MedicalExamDate]) == (true, true), "expired and expiring checkups coexist");
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

    students = StudentRepository.GetPage(new() { Page = 2, PageSize = 2 });
    StudentRepository.MoveToTrash(students.Items.Single().Id);
    Check(StudentRepository.GetPage(new() { Page = 2, PageSize = 2 }).Page == 1, "deleting last row clamps page");
    Check(TrashRepository.GetPage(1, 100).TotalCount == 2, "trash contains deleted entities");
    TrashRepository.SetRetentionDays(0);
    Check(TrashRepository.PurgeExpired() == 0, "automatic trash cleanup can be disabled");

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

    var backup = BackupService.CreateManualBackup(Path.Combine(directory, "backup.db"));
    BackupService.ValidateBackup(backup);
    Check(File.Exists(backup), "backup validates");
    ExpectFailure<InvalidOperationException>(() => BackupService.CreateManualBackup(Db.DbPath), "backup cannot overwrite live database");
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
