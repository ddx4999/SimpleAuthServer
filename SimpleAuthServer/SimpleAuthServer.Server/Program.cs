using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SimpleAuthServer
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("=== Старт сервера ===");

            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                {
                    policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
                });
            });

            var app = builder.Build();
            app.UseCors();

            Console.WriteLine("CORS настроен");

            // ========================= МОДЕЛЬ =========================
            // (оставляем внутри, как вложенные классы, чтобы не мешать top-level)

            // ========================= ЭНДПОИНТЫ =========================
            app.MapPost("/register", async (RegisterRequest request) =>
            {
                Console.WriteLine($"/register: {request.Username}");
                if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
                    return Results.BadRequest("Логин и пароль обязательны.");

                if (request.Password.Length < 3)
                    return Results.BadRequest("Пароль должен быть не короче 3 символов.");

                if (Database.GetUser(request.Username) != null)
                    return Results.Conflict("Пользователь с таким логином уже существует.");

                var newUser = new User
                {
                    Username = request.Username,
                    Password = request.Password,
                    IsAdmin = false,
                    IsBanned = false,
                    IPAddress = request.IPAddress ?? ""
                };

                if (Database.AddUser(newUser))
                {
                    Console.WriteLine($"Зарегистрирован {request.Username}");
                    return Results.Ok(new { message = "Регистрация успешна." });
                }
                else
                    return Results.StatusCode(500);
            });

            app.MapPost("/login", (LoginRequest request) =>
            {
                Console.WriteLine($"/login: {request.Username}");
                if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
                    return Results.BadRequest("Логин и пароль обязательны.");

                var user = Database.GetUser(request.Username);
                if (user == null)
                    return Results.NotFound("Пользователь не найден.");

                if (user.Password != request.Password)
                    return Results.Unauthorized("Неверный пароль.");

                if (user.IsBanned)
                {
                    if (user.BanExpiry.HasValue && user.BanExpiry.Value <= DateTime.Now)
                    {
                        Database.UnbanUser(request.Username);
                        user.IsBanned = false;
                        user.BanExpiry = null;
                    }
                    else
                    {
                        string msg = user.BanExpiry.HasValue ? $"Вы заблокированы до {user.BanExpiry.Value:dd.MM.yyyy HH:mm}." : "Вы заблокированы навсегда.";
                        return Results.BadRequest(new { banned = true, message = msg });
                    }
                }

                return Results.Ok(new
                {
                    username = user.Username,
                    isAdmin = user.IsAdmin,
                    isBanned = user.IsBanned,
                    banExpiry = user.BanExpiry
                });
            });

            app.MapGet("/users", (string? admin) =>
            {
                Console.WriteLine($"/users?admin={admin}");
                if (admin != "true")
                    return Results.Unauthorized("Требуются права администратора.");

                var users = Database.LoadUsers();
                var safeUsers = users.Select(u => new
                {
                    u.Username,
                    u.IsAdmin,
                    u.IsBanned,
                    u.BanExpiry,
                    u.IPAddress
                });
                return Results.Ok(safeUsers);
            });

            app.MapPost("/ban", (BanRequest request, string? admin) =>
            {
                Console.WriteLine($"/ban: {request.Username}, admin={admin}");
                if (admin != "true")
                    return Results.Unauthorized("Требуются права администратора.");

                if (string.IsNullOrWhiteSpace(request.Username))
                    return Results.BadRequest("Укажите пользователя.");

                DateTime? expiry = null;
                if (request.Hours.HasValue && request.Hours.Value > 0)
                    expiry = DateTime.Now.AddHours(request.Hours.Value);

                if (Database.BanUser(request.Username, expiry))
                    return Results.Ok(new { message = $"Пользователь {request.Username} заблокирован." });
                else
                    return Results.NotFound("Пользователь не найден.");
            });

            app.MapPost("/unban", (BanRequest request, string? admin) =>
            {
                Console.WriteLine($"/unban: {request.Username}, admin={admin}");
                if (admin != "true")
                    return Results.Unauthorized("Требуются права администратора.");

                if (string.IsNullOrWhiteSpace(request.Username))
                    return Results.BadRequest("Укажите пользователя.");

                if (Database.UnbanUser(request.Username))
                    return Results.Ok(new { message = $"Пользователь {request.Username} разблокирован." });
                else
                    return Results.NotFound("Пользователь не найден.");
            });

            app.MapPost("/message", (MessageRequest request, string? admin) =>
            {
                Console.WriteLine($"/message: IP={request.IP}, admin={admin}");
                if (admin != "true")
                    return Results.Unauthorized("Требуются права администратора.");

                if (string.IsNullOrWhiteSpace(request.IP) || string.IsNullOrWhiteSpace(request.Message))
                    return Results.BadRequest("IP и сообщение обязательны.");

                Console.WriteLine($"[Сообщение] на IP {request.IP}: {request.Message}");
                return Results.Ok(new { message = $"Сообщение отправлено на {request.IP}." });
            });

            Console.WriteLine("Запускаю сервер на http://0.0.0.0:8080");
            app.Run("http://0.0.0.0:8080");
        }
    }

    // ========================= МОДЕЛЬ ПОЛЬЗОВАТЕЛЯ =========================
    public class User
    {
        public string? Username { get; set; }
        public string? Password { get; set; }
        public bool IsAdmin { get; set; }
        public bool IsBanned { get; set; }
        public DateTime? BanExpiry { get; set; }
        public string? IPAddress { get; set; }
    }

    // ========================= ХРАНИЛИЩЕ =========================
    public static class Database
    {
        private static readonly string FilePath = "users.json";
        private static readonly object LockObj = new object();

        public static List<User> LoadUsers()
        {
            lock (LockObj)
            {
                if (!File.Exists(FilePath))
                {
                    Console.WriteLine("Файл users.json не найден, создаю с админом");
                    var defaultUsers = new List<User>
                    {
                        new User { Username = "admin", Password = "123", IsAdmin = true, IsBanned = false, IPAddress = "127.0.0.1" }
                    };
                    SaveUsers(defaultUsers);
                    return defaultUsers;
                }
                string json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<List<User>>(json) ?? new List<User>();
            }
        }

        public static void SaveUsers(List<User> users)
        {
            lock (LockObj)
            {
                string json = JsonSerializer.Serialize(users, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(FilePath, json);
                Console.WriteLine($"Сохранено {users.Count} пользователей");
            }
        }

        public static User? GetUser(string username)
        {
            var users = LoadUsers();
            return users.Find(u => u.Username != null && u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
        }

        public static bool AddUser(User newUser)
        {
            var users = LoadUsers();
            if (GetUser(newUser.Username!) != null) return false;
            users.Add(newUser);
            SaveUsers(users);
            return true;
        }

        public static bool BanUser(string username, DateTime? expiry = null)
        {
            var users = LoadUsers();
            var user = GetUser(username);
            if (user == null) return false;
            user.IsBanned = true;
            user.BanExpiry = expiry;
            SaveUsers(users);
            return true;
        }

        public static bool UnbanUser(string username)
        {
            var users = LoadUsers();
            var user = GetUser(username);
            if (user == null) return false;
            user.IsBanned = false;
            user.BanExpiry = null;
            SaveUsers(users);
            return true;
        }
    }

    // ========================= DTO =========================
    public class RegisterRequest
    {
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? IPAddress { get; set; }
    }

    public class LoginRequest
    {
        public string? Username { get; set; }
        public string? Password { get; set; }
    }

    public class BanRequest
    {
        public string? Username { get; set; }
        public int? Hours { get; set; }
    }

    public class MessageRequest
    {
        public string? IP { get; set; }
        public string? Message { get; set; }
    }
}
