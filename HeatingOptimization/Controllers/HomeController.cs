using Microsoft.AspNetCore.Mvc;
using Heating.Models;
using Google.OrTools.LinearSolver;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using OfficeOpenXml;

public class HomeController : Controller
{
    public IActionResult Index()
    {
        var fuels = new List<Fuel>
        {
            new Fuel { Name = "Топливо 1", Amount = 240, Heating_values = new List<double> { 4, 3, 12, 2 } },
            new Fuel { Name = "Топливо 2", Amount = 600, Heating_values = new List<double> { 5, 5, 14, 5 } },
            new Fuel { Name = "Топливо 3", Amount = 100, Heating_values = new List<double> { 10, 3, 15, 6 } },
            new Fuel { Name = "Топливо 4", Amount = 200, Heating_values = new List<double> { 11, 10, 10, 7 } },
            new Fuel { Name = "Топливо 5", Amount = 800, Heating_values = new List<double> { 1, 11, 11, 10 } }
        };

        var minLoads = new List<int> { 80, 90, 70, 60 };
        ViewBag.MinLoads = minLoads;
        return View(fuels);
    }

    [HttpPost]
    public IActionResult Calculate(List<Fuel> fuels, int[] minLoads)
    {
        if (minLoads == null || minLoads.Length == 0)
        {
            return Content("Ошибка: минимальные загрузки не заданы.");
        }

        foreach (var fuel in fuels)
        {
            Debug.WriteLine($"Received fuel: {fuel.Name}, Amount: {fuel.Amount}");
            if (string.IsNullOrEmpty(fuel.Name))
            {
                return Content("Ошибка: одно из имен топлив пустое.");
            }
            if (fuel.Amount < 0)
            {
                return Content("Ошибка: количество топлива не может быть меньше нуля.");
            }
            if (fuel.Heating_values == null || fuel.Heating_values.Count == 0)
            {
                return Content("Ошибка: теплотворная способность топлива не задана.");
            }
            Debug.WriteLine($"Heating_values: {string.Join(", ", fuel.Heating_values)}");
        }

        Debug.WriteLine("Received minLoads:");
        foreach (var minLoad in minLoads)
        {
            Debug.WriteLine(minLoad.ToString());
        }

        double totalFuelAvailable = fuels.Sum(f => f.Amount);
        double totalMinLoad = minLoads.Sum();
        if (totalMinLoad > totalFuelAvailable)
        {
            return Content("Ошибка: общая минимальная загрузка агрегатов превышает общее доступное количество топлива.");
        }

        Solver solver = Solver.CreateSolver("GLOP");
        if (solver == null)
        {
            return Content("Ошибка: не удалось создать решатель.");
        }

        int numFuels = fuels.Count;
        int numHeatingUnits = minLoads.Length;

        // Создание переменных для количества топлива в каждом агрегате
        Variable[,] amounts = new Variable[numFuels, numHeatingUnits];
        for (int i = 0; i < numFuels; i++)
        {
            for (int j = 0; j < numHeatingUnits; j++)
            {
                amounts[i, j] = solver.MakeNumVar(0.0, double.PositiveInfinity, $"amount_{i}_{j}");
            }
        }

        // Ограничение: общее количество каждого топлива должно равняться его доступному количеству
        for (int i = 0; i < numFuels; i++)
        {
            Constraint fuelConstraint = solver.MakeConstraint(fuels[i].Amount, fuels[i].Amount, $"fuelConstraint_{i}");
            for (int j = 0; j < numHeatingUnits; j++)
            {
                fuelConstraint.SetCoefficient(amounts[i, j], 1);
            }
        }

        // Ограничение: загрузка каждого агрегата должна быть >= минимальной загрузки
        for (int j = 0; j < numHeatingUnits; j++)
        {
            Constraint unitLoadConstraint = solver.MakeConstraint(minLoads[j], double.PositiveInfinity, $"unitLoadConstraint_{j}");
            for (int i = 0; i < numFuels; i++)
            {
                unitLoadConstraint.SetCoefficient(amounts[i, j], 1);
            }
        }

        // Целевая функция: максимизация общей произведенной теплоты
        Objective objective = solver.Objective();
        for (int i = 0; i < numFuels; i++)
        {
            for (int j = 0; j < numHeatingUnits; j++)
            {
                objective.SetCoefficient(amounts[i, j], fuels[i].Heating_values[j]);
            }
        }
        objective.SetMaximization();

        Solver.ResultStatus resultStatus = solver.Solve();
        if (resultStatus != Solver.ResultStatus.OPTIMAL)
        {
            Debug.WriteLine("Статус результата: " + resultStatus);

            // Дополнительные отладочные сообщения
            for (int i = 0; i < numFuels; i++)
            {
                for (int j = 0; j < numHeatingUnits; j++)
                {
                    Debug.WriteLine($"amounts[{i},{j}] = {amounts[i, j].SolutionValue()}");
                }
            }
            return Content("Ошибка: оптимальное решение не найдено.");
        }

        var results = new List<Result>();
        double totalHeat = 0;
        for (int i = 0; i < numFuels; i++)
        {
            for (int j = 0; j < numHeatingUnits; j++)
            {
                double amount = amounts[i, j].SolutionValue();
                double heatProduced = amount * fuels[i].Heating_values[j];
                totalHeat += heatProduced;
                results.Add(new Result { Name = $"{fuels[i].Name} в агрегате {j + 1}", Amount = amount, HeatProduced = heatProduced });
            }

        }
        ViewBag.Results = results;
        ViewBag.TotalHeat = totalHeat;
        return View("Index", fuels);
    }

    [HttpPost]
    public IActionResult ExportToExcel([FromForm] List<Fuel> fuels, [FromForm] List<int> minLoads, [FromForm] List<Result> results)
    {
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

        using (var package = new ExcelPackage())
        {
            var worksheet = package.Workbook.Worksheets.Add("Results");

            worksheet.Cells[1, 1].Value = "Название топлива";
            for (int i = 0; i < fuels.Count; i++)
            {
                worksheet.Cells[1, i + 2].Value = fuels[i].Name;
            }
            worksheet.Cells[1, fuels.Count + 2].Value = "Минимальная загрузка (т)";

            worksheet.Cells[2, 1].Value = "Количество топлива (т)";
            for (int i = 0; i < fuels.Count; i++)
            {
                worksheet.Cells[2, i + 2].Value = fuels[i].Amount;
            }
            worksheet.Cells[2, fuels.Count + 2].Value = string.Join(", ", minLoads);

            var resultRow = 4;
            worksheet.Cells[resultRow, 1].Value = "Результаты расчета";
            worksheet.Cells[resultRow + 1, 1].Value = "Название топлива";
            worksheet.Cells[resultRow + 1, 2].Value = "Количество (т)";
            worksheet.Cells[resultRow + 1, 3].Value = "Произведенная теплота (МДж)";

            for (int i = 0; i < results.Count; i++)
            {
                worksheet.Cells[resultRow + 2 + i, 1].Value = results[i].Name;
                worksheet.Cells[resultRow + 2 + i, 2].Value = results[i].Amount;
                worksheet.Cells[resultRow + 2 + i, 3].Value = results[i].HeatProduced;
            }
            worksheet.Cells[resultRow + 2 + results.Count, 1].Value = "Общая теплота";
            worksheet.Cells[resultRow + 2 + results.Count, 3].Value = results.Sum(r => r.HeatProduced);

            worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();

            var stream = new MemoryStream();
            package.SaveAs(stream);

            string fileName = "Results.xlsx";
            string contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
            stream.Position = 0;
            return File(stream, contentType, fileName);
        }
    }

}
