using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using System.Data.SQLite;
using Telegram.Bot.Polling;

namespace RaffleBot
{
    class Program
    {
        private static ITelegramBotClient? client;
        private static ReceiverOptions? receiverOptions;
        private static string token = "7006475150:AAF2_c7cH8qVUTguc-dsMv8OVJUqxNLsL4M";
        private static string dbFilePath = "raffles.db";
        private static List<Raffle> raffles = new List<Raffle>();
        private static List<Raffle> raffleHistory = new List<Raffle>();
        private static Timer raffleTimer;
        private static TimeSpan checkInterval = TimeSpan.FromMinutes(1); 
        private static List<long> adminIds = new List<long> { 932635238 };

        public static void Main(string[] args)
        {
            InitializeDatabase(); 

            client = new TelegramBotClient(token);
            receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = new[] { UpdateType.Message, UpdateType.CallbackQuery }
            };

            LoadRaffles();

            raffleTimer = new Timer(CheckRaffles, null, TimeSpan.Zero, checkInterval);

            using var cts = new CancellationTokenSource();
            client.StartReceiving(UpdateHandler, ErrorHandler, receiverOptions, cts.Token);

            Console.WriteLine("Бот работает в автономном режиме!");
            Console.ReadLine();
            Console.WriteLine("Бот остановлен полностью");
        }
        private static InlineKeyboardMarkup StartMenu()
        {
            return new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Актуальные розыгрыши", "raffles") },
                new[] { InlineKeyboardButton.WithCallbackData("История розыгрышей", "show_history") }
            });
        }

        private static void InitializeDatabase()
        {
            if (System.IO.File.Exists(dbFilePath))
            {
                return; 
            }

            SQLiteConnection.CreateFile(dbFilePath);
            using var connection = new SQLiteConnection($"Data Source={dbFilePath}");
            connection.Open();

            using var command = new SQLiteCommand(@"
                CREATE TABLE Raffle (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT UNIQUE NOT NULL,
                    ScheduledTime TEXT,
                    RaffleTime TEXT,
                    Winner TEXT,
                    Participants TEXT
                )", connection);
            command.ExecuteNonQuery();
            command.CommandText = @"
                CREATE TABLE RaffleHistory (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT UNIQUE NOT NULL,
                    ScheduledTime TEXT,
                    RaffleTime TEXT,
                    Winner TEXT,
                    Participants TEXT
                )";
            command.ExecuteNonQuery();
        }
        
        private static void LoadRaffles()
        {
            using var connection = new SQLiteConnection($"Data Source={dbFilePath}");
            connection.Open();
            using var command = new SQLiteCommand("SELECT * FROM Raffle", connection);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                raffles.Add(new Raffle
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    ScheduledTime = !reader.IsDBNull(2) ? TimeSpan.Parse(reader.GetString(2)) : null,
                    RaffleTime = !reader.IsDBNull(3) ? DateTime.Parse(reader.GetString(3)) : null,
                    Winner = !reader.IsDBNull(4) ? reader.GetString(4) : null,
                    Participants = !reader.IsDBNull(5) ? reader.GetString(5).Split(',').ToList() : new List<string>()
                });
            }
            reader.Close();

            // Загрузка истории розыгрышей
            command.CommandText = "SELECT * FROM RaffleHistory";
            using var historyReader = command.ExecuteReader();
            while (historyReader.Read())
            {
                raffleHistory.Add(new Raffle
                {
                    Id = historyReader.GetInt32(0),
                    Name = historyReader.GetString(1),
                    ScheduledTime = !historyReader.IsDBNull(2) ? TimeSpan.Parse(historyReader.GetString(2)) : null,
                    RaffleTime = !historyReader.IsDBNull(3) ? DateTime.Parse(historyReader.GetString(3)) : null,
                    Winner = !historyReader.IsDBNull(4) ? historyReader.GetString(4) : null,
                    Participants = !historyReader.IsDBNull(5) ? historyReader.GetString(5).Split(',').ToList() : new List<string>()
                });
            }
        }

        private static void CheckRaffles(object? state)
        {
            foreach (var raffle in raffles.ToList())
            {
                if (raffle.ScheduledTime <= DateTime.Now.TimeOfDay)
                {
                    _ = SelectWinner(client, raffle);
                    raffles.Remove(raffle);
                    SaveRaffle(raffle, "RaffleHistory");
                    SaveRaffles();
                }
            }
        }

        private static async Task UpdateHandler(ITelegramBotClient client, Update update, CancellationToken token)
        {
            if (update.Type == UpdateType.Message && update.Message?.Text != null)
            {
                var message = update.Message;
                var chatId = message.Chat.Id;

                if (adminIds.Contains(chatId))
                {
                    await HandleAdminCommands(client, message);
                }
                else if (message.Text == "/admin")
                {
                    await client.SendTextMessageAsync(chatId, "** вы не администратор **");
                }

                if (message.Text == "/start")
                {
                    await client.SendTextMessageAsync(message.Chat.Id, $"Привет {message.From?.Username}!", replyMarkup: StartMenu());
                }
            }

            if (update.Type == UpdateType.CallbackQuery)
            {
                await HandleCallbackQuery(client, update.CallbackQuery);
            }
        }

        private static async Task HandleAdminCommands(ITelegramBotClient client, Message message)
        {
            var chatId = message.Chat.Id;

            if (message.Text == "/admin")
            {
                await client.SendTextMessageAsync(chatId, "Вы в панели администратора.\nСоздать розыгрыш: /create name\nУдалить розыгрыш: /delete name\nРедактировать название: /edit oldName newName\nУстановить время: /settime name HH:mm\nЗапустить розыгрыш: /starte name", replyMarkup: AdminPanel());
            }

            if (message.Text.StartsWith("/create"))
            {
                var parts = message.Text.Trim().Split(' ');

                if (parts.Length < 2)
                {
                    await client.SendTextMessageAsync(chatId, "Пожалуйста, укажите название розыгрыша после команды /create");
                    return;
                }

                string giveawayName = string.Join(' ', parts.Skip(1));
                var raffle = new Raffle { Name = giveawayName, ScheduledTime = DateTime.Now.TimeOfDay + TimeSpan.FromMinutes(10) };
                SaveRaffle(raffle);
                raffles.Add(raffle);

                await client.SendTextMessageAsync(chatId, $"Розыгрыш '{giveawayName}' создан! Время начала: {raffle.ScheduledTime}");
            }

            if (message.Text == "/history")
            {
                await ShowRaffleHistory(client, chatId);
            }

            if (message.Text.StartsWith("/delete"))
            {
                var parts = message.Text.Trim().Split(' ');

                if (parts.Length < 2)
                {
                    await client.SendTextMessageAsync(chatId, "Пожалуйста, укажите название розыгрыша после команды /delete");
                    return;
                }

                string giveawayName = string.Join(' ', parts.Skip(1));
                DeleteRaffle(giveawayName);
                var raffle = raffles.FirstOrDefault(r => r.Name.Equals(giveawayName, StringComparison.OrdinalIgnoreCase));
                if (raffle != null)
                {
                    raffles.Remove(raffle);
                    SaveRaffles();
                    await client.SendTextMessageAsync(chatId, $"Розыгрыш '{giveawayName}' удалён.");
                }
                else
                {
                    await client.SendTextMessageAsync(chatId, $"Розыгрыш '{giveawayName}' не найден.");
                }
            }

            if (message.Text.StartsWith("/edit"))
            {
                var parts = message.Text.Trim().Split(' ');

                if (parts.Length < 3)
                {
                    await client.SendTextMessageAsync(chatId, "Использование: /edit OLDname NEWname");
                    return;
                }

                string oldName = parts[1];
                string newName = string.Join(' ', parts.Skip(2));

                var raffle = raffles.FirstOrDefault(r => r.Name.Equals(oldName, StringComparison.OrdinalIgnoreCase));
                if (raffle != null)
                {
                    UpdateRaffleName(raffle, newName);
                    raffle.Name = newName;
                    SaveRaffles();
                    await client.SendTextMessageAsync(chatId, $"Название розыгрышаизменено на '{newName}'.");
                }
                else
                {
                    await client.SendTextMessageAsync(chatId, $"Розыгрыш '{oldName}' не найден.");
                }
            }

            if (message.Text.StartsWith("/settime"))
            {
                var parts = message.Text.Trim().Split(' ');

                if (parts.Length < 3)
                {
                    await client.SendTextMessageAsync(chatId, "Использование: /settime name HH:mm");
                    return;
                }

                string raffleName = parts[1];
                string timeStr = parts[2];

                if (!TimeSpan.TryParse(timeStr, out var scheduledTime))
                {
                    await client.SendTextMessageAsync(chatId, "Пожалуйста, укажите время в формате HH:mm");
                    return;
                }

                var raffle = raffles.FirstOrDefault(r => r.Name.Equals(raffleName, StringComparison.OrdinalIgnoreCase));
                if (raffle != null)
                {
                    UpdateRaffleScheduledTime(raffle, scheduledTime);
                    raffle.ScheduledTime = scheduledTime;
                    SaveRaffles();
                    await client.SendTextMessageAsync(chatId, $"Время розыгрыша \"{raffleName}\" установлено на {timeStr}");
                }
                else
                {
                    await client.SendTextMessageAsync(chatId, $"Розыгрыш '{raffleName}' не найден.");
                }
            }

            if (message.Text.StartsWith("/starte"))
            {
                var parts = message.Text.Trim().Split(' ');

                if (parts.Length < 2)
                {
                    await client.SendTextMessageAsync(chatId, "Пожалуйста, укажите название розыгрыша после команды /starte");
                    return;
                }

                string raffleName = string.Join(' ', parts.Skip(1));
                var raffle = raffles.FirstOrDefault(r => r.Name.Equals(raffleName, StringComparison.OrdinalIgnoreCase));

                if (raffle != null)
                {
                    if (raffle.ScheduledTime <= DateTime.Now.TimeOfDay)
                    {
                        await SelectWinner(client, raffle);
                        raffles.Remove(raffle);
                        SaveRaffle(raffle, "RaffleHistory");
                        SaveRaffles();
                    }
                    else
                    {
                        await client.SendTextMessageAsync(chatId, $"Розыгрыш \"{raffle.Name}\" не может быть запущен до {raffle.ScheduledTime}");
                    }
                }
                else
                {
                    await client.SendTextMessageAsync(chatId, $"Розыгрыш '{raffleName}' не найден.");
                }
            }
        }

        private static async Task HandleCallbackQuery(ITelegramBotClient client, CallbackQuery callbackQuery)
        {
            if (callbackQuery?.Data != null)
            {
                var parts = callbackQuery.Data.Split('_');

                if (callbackQuery.Data == "show_history")
                {
                    await client.DeleteMessageAsync(callbackQuery.Message.Chat.Id, callbackQuery.Message.MessageId);
                    await ShowRaffleHistory(client, callbackQuery.Message.Chat.Id);
                }
                else if (callbackQuery.Data == "close")
                {
                    await client.DeleteMessageAsync(callbackQuery.Message.Chat.Id, callbackQuery.Message.MessageId);
                }
                else if (callbackQuery.Data.StartsWith("history_"))
                {
                    string raffleId = parts[1];
                    var raffle = raffleHistory.FirstOrDefault(r => r.Name == raffleId);
                    if (raffle != null)
                    {
                        var markup = new InlineKeyboardMarkup(new[]
                        {InlineKeyboardButton.WithCallbackData("Назад", "show_history"),
                            InlineKeyboardButton.WithCallbackData("Закрыть", "close")
                        });

                        await client.EditMessageTextAsync(
                            callbackQuery.Message.Chat.Id,
                            callbackQuery.Message.MessageId,
                            $"Розыгрыш: {raffle.Name}\n" +
                            $"Количество участников: {raffle.Participants.Count}\n" +
                            $"Победитель: {raffle.Winner ?? "Неизвестен"}\n" +
                            $"Дата проведения: {raffle.RaffleTime?.ToString("dd.MM.yyyy HH:mm") ?? "Ещё не проведён"}",
                            replyMarkup: markup);
                    }
                }
                else if (callbackQuery.Data == "raffles")
                {
                    await client.DeleteMessageAsync(callbackQuery.Message.Chat.Id, callbackQuery.Message.MessageId);
                    await ShowRaffles(client, callbackQuery.Message.Chat.Id);
                }
                else if (parts.Length == 2)
                {
                    string action = parts[0];
                    string raffleName = parts[1];
                    var raffle = raffles.FirstOrDefault(r => r.Name.Equals(raffleName, StringComparison.OrdinalIgnoreCase));
                    DateTime scheduledDateTime = DateTime.Today.Add(raffle.ScheduledTime.Value); 
                    if (raffle != null && scheduledDateTime > DateTime.Now && raffle.RaffleTime == null)
                    {
                        long participantId = callbackQuery.From.Id;
                        if (action == "participate")
                        {
                            if (!raffle.ParticipantIds.Contains(participantId))
                            {
                                UpdateRaffleParticipants(raffle, participantId, callbackQuery.From.Username ?? "Anonymous");
                                SaveRaffles();
                                await client.AnswerCallbackQueryAsync(callbackQuery.Id, "Вы успешно участвуете в розыгрыше!");
                            }
                            else
                            {
                                await client.AnswerCallbackQueryAsync(callbackQuery.Id, "Вы уже участвуете в этом розыгрыше.");
                            }
                        }
                        else if (action == "withdraw")
                        {
                            if (raffle.ParticipantIds.Contains(participantId))
                            {
                                RemoveRaffleParticipant(raffle, participantId);
                                SaveRaffles();
                                await client.AnswerCallbackQueryAsync(callbackQuery.Id, "Вы покинули розыгрыш.");
                            }
                            else
                            {
                                await client.AnswerCallbackQueryAsync(callbackQuery.Id, "Вы не участвуете в этом розыгрыше.");
                            }
                        }
                        await client.EditMessageTextAsync(callbackQuery.Message.Chat.Id, callbackQuery.Message.MessageId,
                            $"Розыгрыш: {raffle.Name}\nКоличество участников: {raffle.Participants.Count}",
                            replyMarkup: RaffleActionButtons(raffleName));
                    }
                    else
                    {
                        await client.AnswerCallbackQueryAsync(callbackQuery.Id, "Розыгрыш уже завершён или не найден.");
                        await client.DeleteMessageAsync(callbackQuery.Message.Chat.Id, callbackQuery.Message.MessageId);
                    }
                }
            }
        }

        private static async Task ShowRaffleHistory(ITelegramBotClient client, long chatId)
        {
            if (!raffleHistory.Any())
            {
                await client.SendTextMessageAsync(chatId, "История розыгрышей пуста.");
            }
            else
            {
                var buttons = raffleHistory.Select(r =>
                    InlineKeyboardButton.WithCallbackData(r.Name, $"history_{r.Name}")).ToList();

                var keyboard = new InlineKeyboardMarkup(buttons.Concat(new[] { InlineKeyboardButton.WithCallbackData("Закрыть", "close") }));
                await client.SendTextMessageAsync(chatId, "Выберите розыгрыш из истории:", replyMarkup: keyboard);
            }
        }

        private static async Task ShowRaffles(ITelegramBotClient client, long chatId)
        {
            if (!raffles.Any())
            {
                await client.SendTextMessageAsync(chatId, "На данный момент сейчас не доступны розыгрыши.");
            }
            else
            {
                foreach (var raffle in raffles)
                {
                    var keyboard = RaffleActionButtons(raffle.Name);

                    await client.SendTextMessageAsync(chatId,
                        $"Розыгрыш: {raffle.Name}\nКоличество участников: {raffle.Participants.Count}\nЗапланированное время: {raffle.ScheduledTime}",
                        replyMarkup: keyboard);
                }
            }
        }

        private static InlineKeyboardMarkup RaffleActionButtons(string raffleName)
        {
            return new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("Участвовать", $"participate_{raffleName}"),
                    InlineKeyboardButton.WithCallbackData("Отписаться", $"withdraw_{raffleName}")
                }
            });
        }

        // Сохранение розыгрышей в базу данных
        private static void SaveRaffles()
        {
            using var connection = new SQLiteConnection($"Data Source={dbFilePath}");
            connection.Open();

            foreach (var raffle in raffles)
            {
                using var command = new SQLiteCommand(
                    "INSERT OR REPLACE INTO Raffle (Id, Name, ScheduledTime, RaffleTime, Winner, Participants) VALUES (@Id, @Name, @ScheduledTime, @RaffleTime, @Winner, @Participants)", connection);
                command.Parameters.AddWithValue("@Id", raffle.Id);
                command.Parameters.AddWithValue("@Name", raffle.Name);
                command.Parameters.AddWithValue("@ScheduledTime", raffle.ScheduledTime?.ToString());
                command.Parameters.AddWithValue("@RaffleTime", raffle.RaffleTime?.ToString());
                command.Parameters.AddWithValue("@Winner", raffle.Winner);
                command.Parameters.AddWithValue("@Participants", string.Join(",", raffle.Participants));
                command.ExecuteNonQuery();
            }
        }

        // Сохранение розыгрыша в определенную таблицу
        private static void SaveRaffle(Raffle raffle, string tableName = "Raffle")
        {
            using var connection = new SQLiteConnection($"Data Source={dbFilePath}");
            connection.Open();

            using var command = new SQLiteCommand(
                $"INSERT OR REPLACE INTO {tableName} (Id, Name, ScheduledTime, RaffleTime, Winner, Participants) VALUES (@Id, @Name, @ScheduledTime, @RaffleTime, @Winner, @Participants)", connection);
            command.Parameters.AddWithValue("@Id", raffle.Id);
            command.Parameters.AddWithValue("@Name", raffle.Name);
            command.Parameters.AddWithValue("@ScheduledTime", raffle.ScheduledTime?.ToString());
            command.Parameters.AddWithValue("@RaffleTime", raffle.RaffleTime?.ToString());
            command.Parameters.AddWithValue("@Winner", raffle.Winner);
            command.Parameters.AddWithValue("@Participants", string.Join(",", raffle.Participants));
            command.ExecuteNonQuery();
        }

        // Удаление розыгрыша из базы данных
        private static void DeleteRaffle(string raffleName)
        {
            using var connection = new SQLiteConnection($"Data Source={dbFilePath}");
            connection.Open();

            using var command = new SQLiteCommand("DELETE FROM Raffle WHERE Name = @Name", connection);
            command.Parameters.AddWithValue("@Name", raffleName);
            command.ExecuteNonQuery();
        }

        // Обновление названия розыгрыша
        private static void UpdateRaffleName(Raffle raffle, string newName)
        {
            using var connection = new SQLiteConnection($"Data Source={dbFilePath}");
            connection.Open();

            using var command = new SQLiteCommand("UPDATE Raffle SET Name = @NewName WHERE Id = @Id", connection);
            command.Parameters.AddWithValue("@NewName", newName);
            command.Parameters.AddWithValue("@Id", raffle.Id);
            command.ExecuteNonQuery();
        }

        // Обновление запланированного времени розыгрыша
        private static void UpdateRaffleScheduledTime(Raffle raffle, TimeSpan newScheduledTime)
        {
            using var connection = new SQLiteConnection($"Data Source={dbFilePath}");
            connection.Open();

            using var command = new SQLiteCommand("UPDATE Raffle SET ScheduledTime = @NewScheduledTime WHERE Id = @Id", connection);
            command.Parameters.AddWithValue("@NewScheduledTime", newScheduledTime.ToString());
            command.Parameters.AddWithValue("@Id", raffle.Id);
            command.ExecuteNonQuery();
        }

        // Добавление участника в розыгрыш
        private static void UpdateRaffleParticipants(Raffle raffle, long participantId, string participantName)
        {
            raffle.ParticipantIds.Add(participantId);
            raffle.Participants.Add(participantName);
            using var connection = new SQLiteConnection($"Data Source={dbFilePath}");
            connection.Open();

            using var command = new SQLiteCommand("UPDATE Raffle SET Participants = @Participants WHERE Id = @Id", connection);
            command.Parameters.AddWithValue("@Participants", string.Join(",", raffle.Participants));
            command.Parameters.AddWithValue("@Id", raffle.Id);
            command.ExecuteNonQuery();
        }

        // Удаление участника из розыгрыша
        private static void RemoveRaffleParticipant(Raffle raffle, long participantId)
        {
            raffle.ParticipantIds.Remove(participantId);


            int index = raffle.ParticipantIds.IndexOf(participantId);
            if (index != -1)
            {
                raffle.Participants.RemoveAt(index);
            }

            using var connection = new SQLiteConnection($"Data Source={dbFilePath}");
            connection.Open();

            using var command = new SQLiteCommand("UPDATE Raffle SET Participants = @Participants WHERE Id = @Id", connection);
            command.Parameters.AddWithValue("@Participants", string.Join(",", raffle.Participants));
            command.Parameters.AddWithValue("@Id", raffle.Id);
            command.ExecuteNonQuery();
        }

        private static InlineKeyboardMarkup AdminPanel()
        {
            return new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Запустить розыгрыш", "/starte name") }
            });
        }

        private static Task ErrorHandler(ITelegramBotClient client, Exception exception, CancellationToken token)
        {
            Console.WriteLine($"Ошибка: {exception.Message}");
            return Task.CompletedTask;
        }
        private static async Task SelectWinner(ITelegramBotClient client, Raffle raffle)
        {
            if (raffle.Participants.Any())
            {
                raffle.RaffleTime = DateTime.Now;  

                Random rand = new Random();
                int winnerIndex = rand.Next(raffle.Participants.Count);
                raffle.Winner = raffle.Participants[winnerIndex];
                long winnerId = raffle.ParticipantIds[winnerIndex];

                string messageText = $"Поздравляем! Вы победитель розыгрыша '{raffle.Name}'!";
                await client.SendTextMessageAsync((int)winnerId, messageText);

                foreach (var participantId in raffle.ParticipantIds)
                {
                    if (participantId != winnerId)
                    {
                        await client.SendTextMessageAsync((int)participantId, $"Вы не выиграли в розыгрыше '{raffle.Name}'. Спасибо за участие!");
                    }
                }

                string participantList = string.Join(", ", raffle.Participants);
                foreach (var participantId in raffle.ParticipantIds)
                {
                    await client.SendTextMessageAsync((int)participantId, $"Результаты розыгрыша '{raffle.Name}'\nПобедитель - {raffle.Winner}.\n\n\nПолный список участников: {participantList}");
                }
            }
            else
            {
                string noParticipantsMessage = $"В розыгрыше '{raffle.Name}' нет участников.";
                foreach (var participantId in raffle.ParticipantIds)
                {
                    await client.SendTextMessageAsync((int)participantId, noParticipantsMessage);
                }
            }
        }

        public class Raffle
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public List<string> Participants { get; set; } = new List<string>();
            public List<long> ParticipantIds { get; set; } = new List<long>();
            public TimeSpan? ScheduledTime { get; set; }
            public DateTime? RaffleTime { get; set; }
            public string Winner { get; set; }
        }
    }
}
