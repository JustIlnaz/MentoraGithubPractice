using System;
using System.IO;
using System.Text.Json;

namespace mentora.Models;

public static class DbConfig
{
    private static readonly Lazy<string> Value = new(Load);

    public static string ConnectionString => Value.Value;

    private static string Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "dbsettings.json");

        if (!File.Exists(path))
            throw new InvalidOperationException("Не найден файл dbsettings.json рядом с программой.");

        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);

        string? value = document.RootElement.GetProperty("ConnectionString").GetString();

        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("В dbsettings.json не задана строка подключения.");

        var settings = new Npgsql.NpgsqlConnectionStringBuilder(value);


        System.Diagnostics.Debug.WriteLine($"Файл: {path}");
        System.Diagnostics.Debug.WriteLine($"Сервер: {settings.Host}");
        System.Diagnostics.Debug.WriteLine($"Порт: {settings.Port}");
        System.Diagnostics.Debug.WriteLine($"База: {settings.Database}");
        System.Diagnostics.Debug.WriteLine($"Пользователь: {settings.Username}");

        return value;
    }
}