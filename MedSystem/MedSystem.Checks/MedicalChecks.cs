using MedSystem.Core.Models;
using MedSystem.Core.Validation;
using MedSystem.Data;
using MedSystem.Data.Repositories;
using Microsoft.Data.Sqlite;

public static class MedicalChecks
{
    public static void Run()
    {
        const string customSpecialty = "Тестовая специальность";
        SpecialtyRepository.Save(null, customSpecialty);
        try
        {
            SpecialtyRepository.Save(null, customSpecialty.ToUpperInvariant());
            throw new Exception("Каталог принял дубликат с другим регистром.");
        }
        catch (InvalidOperationException) { }

        var student = new Student
        {
            LastName = "Проверочный",
            FirstName = "Студент",
            HealthGroup = "Прежняя группа",
            IsOnDispensaryRegister = true,
            DispensaryDetails = "Прежняя запись",
            Observations =
            [
                new() { Specialty = customSpecialty, IcdCode = "A00", Diagnosis = "Пример диагноза" },
                new() { Specialty = "Ручной ввод", IcdCode = "A01", Diagnosis = "Другой диагноз" },
            ],
        };

        StudentRepository.Insert(student);
        SpecialtyRepository.Save(customSpecialty, "Переименованная специальность");
        SpecialtyRepository.Delete("Переименованная специальность");
        var listed = StudentRepository.GetPage(new() { SearchText = student.FullName }).Items.Single();
        var saved = StudentRepository.GetById(listed.Id)!;
        if (listed.Observations.Count != 2 || saved.Observations.Count != 2 ||
            saved.HealthGroup != student.HealthGroup || saved.DispensaryDetails != student.DispensaryDetails ||
            !saved.Observations.Select(o => o.Display).SequenceEqual(student.Observations.Select(o => o.Display)) ||
            !saved.DispensaryDisplay.Contains("Прежняя запись") || !saved.DispensaryDisplay.Contains("A01"))
            throw new Exception("Данные диспансерного учёта изменились после чтения или правок каталога.");

        saved.Observations[1].IcdCode = "";
        if (Validators.ValidateStudent(saved).Count == 0)
            throw new Exception("Пустой код диагноза прошёл проверку.");
        try
        {
            StudentRepository.Update(saved);
            throw new Exception("Неполное наблюдение сохранено.");
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19) { }
        if (StudentRepository.GetById(saved.Id)!.Observations[1].IcdCode != "A01")
            throw new Exception("Неуспешная запись изменила наблюдения.");

        saved.Observations.RemoveAt(1);
        StudentRepository.Update(saved);
        if (StudentRepository.GetById(saved.Id)!.Observations.Count != 1)
            throw new Exception("Удаление отдельного наблюдения не сохранилось.");

        StudentRepository.MoveToTrash(saved.Id);
        TrashRepository.Restore("student", saved.Id);
        if (StudentRepository.GetById(saved.Id)!.Observations.Count != 1)
            throw new Exception("После восстановления из корзины потеряно наблюдение.");
        saved.Observations.Clear();
        StudentRepository.Update(saved);
        var legacy = StudentRepository.GetById(saved.Id)!;
        if (!legacy.IsOnDispensaryRegister || legacy.DispensaryDisplay != "Да — Прежняя запись" ||
            legacy.Observations.Count != 0 || legacy.HealthGroup != "Прежняя группа")
            throw new Exception("Прежняя запись не сохранилась без назначенного диагноза.");
        legacy.Observations.Add(new() { Specialty = legacy.DispensaryDetails, IcdCode = "A00", Diagnosis = "Пример диагноза" });
        legacy.DispensaryDetails = "";
        StudentRepository.Update(legacy);
        StudentRepository.Update(StudentRepository.GetById(legacy.Id)!);
        if (StudentRepository.GetById(legacy.Id) is not { DispensaryDetails: "", Observations.Count: 1 })
            throw new Exception("Повторное сохранение дублировало прежнюю запись.");

        saved.IsOnDispensaryRegister = false;
        StudentRepository.Update(saved);
        if (StudentRepository.GetById(saved.Id) is not { Observations.Count: 0, DispensaryDetails: "" })
            throw new Exception("Снятие с диспансерного учёта не очистило запись.");

        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM students WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", saved.Id);
        cmd.ExecuteNonQuery();
    }
}
