
using System.Data.SQLite;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

class Program
{
    private static ITelegramBotClient? client;
    private static ReceiverOptions? receiverOptions;
    private static string token = "7006475150:AAF2_c7cH8qVUTguc-dsMv8OVJUqxNLsL4M";
    private static InlineKeyboardMarkup? keyboard;
    private static InlineKeyboardMarkup? adminKeyboard;
    private static InlineKeyboardMarkup? raffle;
    private static List<long> adminIds = new List<long> { 932635238 };
    private static string dbPath = "raffle.db"; 

    public static void Main(string[] args)
    {
        client = new TelegramBotClient(token);
        receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = new[]
            {
                UpdateType.Message,
                UpdateType.CallbackQuery
            }
        };

        using var cts = new CancellationTokenSource();
        client.StartReceiving(UpdateHandler, ErrorHandler, receiverOptions, cts.Token);

    
        InitializeDatabase();
        LoadRafflesIntoKeyboard();

        keyboard = new InlineKeyboardMarkup(
            new List<InlineKeyboardButton[]>()
            {
                new InlineKeyboardButton[]
                {
                    InlineKeyboardButton.WithCallbackData("Розыгрыш 1", "button1"),
                    InlineKeyboardButton.WithCallbackData("Розыгрыш 2", "button2"),
                    InlineKeyboardButton.WithCallbackData("Розыгрыш 3", "button3"),
                }
            });

        raffle = new InlineKeyboardMarkup(
            new List<InlineKeyboardButton[]>()
            {
                new InlineKeyboardButton[]
                {
                    InlineKeyboardButton.WithCallbackData("Хочу участвовать!", "participate1"),
                }
            });

        // Панель управления для администраторов
        adminKeyboard = new InlineKeyboardMarkup(
            new List<InlineKeyboardButton[]>()
            {
                new InlineKeyboardButton[]
                {
                    InlineKeyboardButton.WithCallbackData("Создать розыгрыш", "createRaffle"),
                    InlineKeyboardButton.WithCallbackData("Редактировать розыгрыш", "editRaffle"),
                }
            });
        Console.WriteLine("Бот запущен");
        Console.ReadLine();
        Console.WriteLine("Бот остановлен");
    }

    // Инициализация базы данных SQLite
    private static void InitializeDatabase()
    {
        using var connection = new SQLiteConnection($"Data Source={dbPath}");
        try
        {
            connection.Open();

            ExecuteCommand(connection, @"
      CREATE TABLE IF NOT EXISTS raffles (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        name TEXT UNIQUE NOT NULL,
        description TEXT,
        image TEXT
      );
    ");

            ExecuteCommand(connection, @"
      CREATE TABLE IF NOT EXISTS participants (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        raffle_id INTEGER REFERENCES raffles(id),
        user_id BIGINT NOT NULL
      );
    ");

            Console.WriteLine("Database initialized successfully!");

        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error initializing database: {ex.Message}");
        }
    }
    private static void ExecuteCommand(SQLiteConnection connection, string commandText)
    {
        using (var command = new SQLiteCommand(commandText, connection))
        {
            command.ExecuteNonQuery();
        }
    }

    // Добавление участника в розыгрыш
    private static async Task AddParticipantToRaffle(string raffleName, long userId)
    {
        using var connection = new SQLiteConnection($"Data Source={dbPath}");
        connection.Open();
        var raffleId = GetRaffleId(connection, raffleName);

        // Проверяем, добавлен ли уже участник
        var isParticipant = IsParticipant(connection, raffleId, userId);
        if (isParticipant)
        {
            await client.SendTextMessageAsync(userId, $"Вы уже участвуете в {raffleName}.");
        }
        else
        {
            AddParticipant(connection, raffleId, userId);
            await client.SendTextMessageAsync(userId, $"Вы успешно добавлены в розыгрыш {raffleName}!");
        }
    }


    private static int GetRaffleId(SQLiteConnection connection, string raffleName)
    {
        using var command = new SQLiteCommand("SELECT id FROM raffles WHERE name = @name", connection);
        command.Parameters.AddWithValue("@name", raffleName);
        object result = command.ExecuteScalar();
        if (result == null)
        {
            return -1; 
        }
        else
        {
            return (int)result;
        }
    }
    // Проверка, является ли пользователь участником
    private static bool IsParticipant(SQLiteConnection connection, int raffleId, long userId)
    {
        using var command = new SQLiteCommand("SELECT COUNT(*) FROM participants WHERE raffle_id = @raffleId AND user_id = @userId", connection);
        command.Parameters.AddWithValue("@raffleId", raffleId);
        command.Parameters.AddWithValue("@userId", userId);
        return (long)command.ExecuteScalar() > 0;
    }
    private static void LoadRafflesIntoKeyboard()
    {
        using var connection = new SQLiteConnection($"Data Source={dbPath}");
        connection.Open();

        var raffles = GetRaffleNames(connection);

        keyboard = new InlineKeyboardMarkup(raffles.Select(r => new InlineKeyboardButton[]
        {
            InlineKeyboardButton.WithCallbackData(r, $"button {r}")
        }).ToList());
    }
    private static List<string> GetRaffleNames(SQLiteConnection connection)
    {
        using var command = new SQLiteCommand("SELECT name FROM raffles", connection);
        using var reader = command.ExecuteReader();

        var raffleNames = new List<string>();
        while (reader.Read())
        {
            raffleNames.Add(reader.GetString(0));
        }

        return raffleNames;
    }


    private static async Task StartRaffle(string raffleName)
    {
        using var connection = new SQLiteConnection($"Data Source={dbPath}");
        connection.Open();
        var raffleId = GetRaffleId(connection, raffleName);
        if (raffleId != -1)
        {
            await client.SendTextMessageAsync(adminIds[0], $"Розыгрыш {raffleName} уже существует.");
            return;
        }
        ExecuteCommand(connection, $"INSERT INTO raffles (name) VALUES ('{raffleName}')");

        // Обновление клавиатуры
        LoadRafflesIntoKeyboard();

        await client.SendTextMessageAsync(adminIds[0], $"Розыгрыш {raffleName} запущен!");
    }

    private static async Task StopRaffle(string raffleName)
    {
        using var connection = new SQLiteConnection($"Data Source={dbPath}");
        connection.Open();
        using var command = new SQLiteCommand("DELETE FROM raffles WHERE name = @raffleName", connection);
        command.Parameters.AddWithValue("@raffleName", raffleName);
        int rowsAffected = command.ExecuteNonQuery();

        if (rowsAffected > 0)
        {
            LoadRafflesIntoKeyboard();
            await client.SendTextMessageAsync(adminIds[0], $"Розыгрыш {raffleName} остановлен!");
        }
        else
        {
            await client.SendTextMessageAsync(adminIds[0], $"Розыгрыша {raffleName} не существует.");
        }
    }

    // Обработка ошибок
    private static Task ErrorHandler(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        Console.WriteLine(exception.Message);
        return Task.CompletedTask;
    }
 

    // Обработка текстовых сообщений
    private static async Task HandleMessage(ITelegramBotClient botClient, Message message)
    {
        // Обработка команд от админов
        if (adminIds.Contains(message.From.Id))
        {
            if (message.Text.StartsWith("/startraffle"))
            {
                var raffleName = message.Text.Split(' ')[1];
                await StartRaffle(raffleName);
            }
            else if (message.Text.StartsWith("/stopraffle"))
            {
                var raffleName = message.Text.Split(' ')[1];
                await StopRaffle(raffleName);
            }
        }
        // Отправка приветственного сообщения
        else if (message.Text == "/start")
        {
            await botClient.SendTextMessageAsync(message.Chat.Id, "Добро пожаловать! Выберите розыгрыш:", replyMarkup: keyboard);
        }
        // Отправка информации о боте
        else if (message.Text == "/help")
        {
            await botClient.SendTextMessageAsync(message.Chat.Id, "Этот бот позволяет участвовать в розыгрышах. Используйте команду /start, чтобы начать");
        }
    }


    private static async Task CreateRaffle(ITelegramBotClient client, Message message, SQLiteConnection connection)
    {
        // Получение данных от пользователя (название, описание, картинка)
        await client.SendTextMessageAsync(message.Chat.Id, "Введите название розыгрыша:");
        var raffleName = await WaitForMessage(client, message.Chat.Id);

        await client.SendTextMessageAsync(message.Chat.Id, "Введите описание розыгрыша:");
        var raffleDescription = await WaitForMessage(client, message.Chat.Id);

        await client.SendTextMessageAsync(message.Chat.Id, "Введите ссылку на изображение (необязательно):");
        var raffleImage = await WaitForMessage(client, message.Chat.Id);

        // Сохранение розыгрыша в базе данных
        using (var command = new SQLiteCommand("INSERT INTO raffles (name, description, image) VALUES (@name, @description, @image)", connection))
        {
            command.Parameters.AddWithValue("@name", raffleName);
            command.Parameters.AddWithValue("@description", raffleDescription);
            command.Parameters.AddWithValue("@image", raffleImage);
            command.ExecuteNonQuery();
        }

        // Вывод сообщения о создании розыгрыша
        await client.SendTextMessageAsync(message.Chat.Id, "Розыгрыш успешно создан!");
    }
    private static async Task EditRaffle(ITelegramBotClient client, Message message, SQLiteConnection connection)
    {
        // Получение ID розыгрыша для редактирования
        await client.SendTextMessageAsync(message.Chat.Id, "Введите ID розыгрыша для редактирования:");
        var raffleIdStr = await WaitForMessage(client, message.Chat.Id);

        if (!int.TryParse(raffleIdStr, out int raffleId))
        {
            await client.SendTextMessageAsync(message.Chat.Id, "Некорректный ID розыгрыша.");
            return;
        }

        // Проверка существования розыгрыша (исправление)
        if (GetRaffleId(connection, raffleId.ToString()) == -1)
        {
            await client.SendTextMessageAsync(message.Chat.Id, $"Розыгрыш с ID {raffleId} не найден.");
            return;
        }

        // Получение данных для редактирования
        await client.SendTextMessageAsync(message.Chat.Id, "Введите новое название розыгрыша (оставьте пустым, чтобы не менять):");
        var newRaffleName = await WaitForMessage(client, message.Chat.Id);

        await client.SendTextMessageAsync(message.Chat.Id, "Введите новое описание розыгрыша (оставьте пустым, чтобы не менять):");
        var newRaffleDescription = await WaitForMessage(client, message.Chat.Id);

        await client.SendTextMessageAsync(message.Chat.Id, "Введите новую ссылку на изображение (оставьте пустым, чтобы не менять):");
        var newRaffleImage = await WaitForMessage(client, message.Chat.Id);

        // Обновление данных розыгрыша в базе данных
        using (var command = new SQLiteCommand("UPDATE raffles SET name = @name, description = @description, image = @image WHERE id = @id", connection))
        {
            command.Parameters.AddWithValue("@name", string.IsNullOrEmpty(newRaffleName) ? null : newRaffleName);
            command.Parameters.AddWithValue("@description", string.IsNullOrEmpty(newRaffleDescription) ? null : newRaffleDescription);
            command.Parameters.AddWithValue("@image", string.IsNullOrEmpty(newRaffleImage) ? null : newRaffleImage);
            command.Parameters.AddWithValue("@id", raffleId);
            command.ExecuteNonQuery();
        }

        // Вывод сообщения о редактировании розыгрыша
        await client.SendTextMessageAsync(message.Chat.Id, $"Розыгрыш с ID {raffleId} успешно отредактирован!");
    }

    // Метод для ожидания сообщения от пользователя
    private static async Task<string> WaitForMessage(ITelegramBotClient client, long chatId)
    {
        var message = await client.SendTextMessageAsync(chatId,"");
        return message.Text;
    }



    private static SQLiteConnection GetConnection()
    {
        return new SQLiteConnection($"Data Source={dbPath}");
    }

    // Добавление участника в базу данных
    private static void AddParticipant(SQLiteConnection connection, int raffleId, long userId)
    {
        using var command = new SQLiteCommand("INSERT INTO participants (raffle_id, user_id) VALUES (@raffleId, @userId)", connection);
        command.Parameters.AddWithValue("@raffleId", raffleId);
        command.Parameters.AddWithValue("@userId", userId);
        command.ExecuteNonQuery();
    }

    // Обработчик обновлений
    private static async Task UpdateHandler(ITelegramBotClient client, Update update, CancellationToken cancellationToken)
    {
        if (update.Type == UpdateType.Message && update.Message.Text != null)
        {
            var message = update.Message;

            if (message.Text == "/start")
            {
                Console.WriteLine($"Получено сообщение от пользователя {message.From.Username} с Id: {message.From.Id}");
                await client.SendTextMessageAsync(message.Chat.Id, $"Привет {message.From.Username}!");
                await client.SendTextMessageAsync(message.Chat.Id,
                    "Это бот для участия в розыгрышах.\nЧтобы испытать свою удачу, выбери в каком розыгрыше ты хочешь участвовать:",
                    replyMarkup: keyboard);

                // Проверка, является ли пользователь администратором
                if (adminIds.Contains(message.From.Id))
                {
                    await client.SendTextMessageAsync(message.Chat.Id, "Вы администратор. У вас есть доступ к редактированию розыгрышей.");
                    await client.SendTextMessageAsync(message.Chat.Id, "Панель управления:", replyMarkup: adminKeyboard);
                }
            }
           
        }



        if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery.Data != null)
        {
            var callbackQuery = update.CallbackQuery;
            var user = callbackQuery.From;

            // Проверка на выбранную кнопку
            switch (callbackQuery.Data)
            {
                case "button1":
                    await client.SendTextMessageAsync(callbackQuery.Message.Chat.Id, "Вы выбрали: Розыгрыш 1", replyMarkup: raffle);
                    break;

                case "participate1":
                    await AddParticipantToRaffle("Розыгрыш 1", user.Id);
                    break;
                case "createRaffle":
                    // Запрашиваем название нового розыгрыша
                    await CreateRaffle(client, callbackQuery.Message, GetConnection());

                    break;

                case "editRaffle":
                    // Запрашиваем ID розыгрыша для редактирования
                    await client.SendTextMessageAsync(callbackQuery.From.Id, "Введите ID розыгрыша для редактирования:");
            
                    break;

            }
        }
    }


    


}
