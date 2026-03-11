namespace TgDataPlanner.Configuration
{
    /// <summary>
    /// Централизованное хранилище констант, сообщений и параметров конфигурации бота.
    /// Все строковые литералы, числовые параметры и шаблоны сообщений вынесены сюда
    /// для упрощения поддержки, локализации и настройки без изменения бизнес-логики.
    /// </summary>
    public static class BotConstants
    {
        #region Числовые параметры конфигурации
        /// <summary>
        /// Порог подтверждения сессии: минимальная доля игроков (0.0–1.0),
        /// которые должны подтвердить участие для проведения сессии.
        /// Значение 0.75 означает 75%.
        /// </summary>
        public const double ConfirmationThreshold = 0.75;
        /// <summary>
        /// Минимальная длительность окна планирования в часах.
        /// </summary>
        public const int MinPlanningDurationHours = 3;
        /// <summary>
        /// Максимальное количество вариантов окон для отображения пользователю.
        /// </summary>
        public const int MaxPlanningResultsToShow = 5;
        /// <summary>
        /// Стандартная длительность игровой сессии в часах.
        /// </summary>
        public const int DefaultSessionDurationHours = 3;
        /// <summary>
        /// Окно поиска рекомендаций в часах (по умолчанию — 1 неделя).
        /// </summary>
        public const double RecommendationSearchWindowHours = 168;
        /// <summary>
        /// Шаг перебора времени для поиска рекомендаций в минутах.
        /// </summary>
        public const int RecommendationTimeStepMinutes = 60;
        /// <summary>
        /// Максимальная длина обрезки текста для логирования.
        /// </summary>
        public const int LogTruncateLength = 100;
        /// <summary>
        /// Максимальная длина текста для превью в логах.
        /// </summary>
        public const int LogPreviewLength = 50;
        #endregion
        #region Префиксы callback-данных
        /// <summary>
        /// Префиксы для маршрутизации callback-запросов.
        /// </summary>
        public static class CallbackPrefixes
        {
            public const string CancelAction = "cancel_action";
            public const string SetTimeZone = "set_tz_";
            public const string PickDate = "pick_date_";
            public const string ToggleTime = "toggle_time_";
            public const string BackToDates = "back_to_dates";
            public const string StartPlan = "start_plan_";
            public const string FinishVoting = "finish_voting";
            public const string JoinGroup = "join_group_";
            public const string ConfirmDeleteGroup = "confirm_delete_";
            public const string ConfirmTime = "confirm_time_";
            public const string LeaveGroup = "leave_group_";
            public const string RsvpYes = "rsvp_yes_";
            public const string RsvpNo = "rsvp_no_";
            public const string StartRequest = "start_request_";
            public const string SelectRecommendation = "select_rec_";
            public const string ViewGroupMembers = "view_members_";
            public const string ViewSessionInfo = "view_session_";
            public const string StartMenuFree = "start_free";
            public const string StartMenuTimeZone = "start_timezone";
            public const string StartMenuStatus = "start_status";
            public const string StartMenuJoin = "start_join";
            public const string StartMenuPlan = "start_plan";
            public const string StartMenuHelp = "start_help";
            public const string ShowHelpMenu = "show_help";
        }
        #endregion
        #region Сообщения для пользователей
        /// <summary>
        /// Сообщения, отображаемые администраторам (Мастерам).
        /// </summary>
        public static class AdminMessages
        {
            public const string AdminOnlyAction = "🔒 Только Мастер Подземелий обладает властью выполнить это действие";
            public const string AdminOnlyPlanning = "🔒 Лишь Мастер может бросить кубы планирования";
            public const string AdminOnlyRecommendations = "🔒 Только Мастер может вопрошать о рекомендациях";
            public const string AdminOnlyCancel = "🔒 Лишь Мастер может отменить назначенную сессию";
            public const string AdminOnlyDeleteGroup = "🔒 Только Мастер может распустить группу";
            public const string AdminOnlyRequestFreeTime = "🔒 Лишь Мастер может запросить расписание героев";
            public const string AdminOnlySelectRecommendation = "🔒 Только Мастер может избрать рекомендацию";
            public const string AdminOnlyViewMembers = "🔒 Лишь Мастер может узреть состав группы";
            public const string GroupCreated = "✅ **Группа \"{0}\" создана!**\nДа начнётся новое приключение!";
            public const string GroupDeleted = "🗑 **Группа \"{0}\" распущена.**\nИстория этой партии завершена.";
            public const string SessionConfirmed = "🎉 **СЕССИЯ ПОДТВЕРЖДЕНА!**\n👥 Группа: **{0}**\n📅 Дата: **{1}** (МСК)\n✅ Героев готово: {2}/{3} ({4:P0})\n{5}\nГотовьте кубы и листы персонажей! ⚔️🎲";
            public const string SessionConfirmedWithPlayers = "🎉 **СЕССИЯ ПОДТВЕРЖДЕНА!**\n👥 Группа: **{0}**\n📅 Дата: **{1}**\n🕒 Время: **{2}** (по МСК)\n\n📋 **Отряд героев:**\n{3}\n\n✅ Готовы к бою: {4}/{5} ({6:P0})\n{7}\nУвидимся за игровым столом! ⚔️";
            public const string SessionCancelled = "😔 **СЕССИЯ ОТМЕНЕНА**\nУвы, недостаточно героев откликнулось ({0:P0}).\nМастерь получит уведомление и, возможно, бросит кубы планирования заново.";
            public const string SessionRescheduled = "⚠️ **ТРЕБУЕТСЯ ПЕРЕПЛАНИРОВАНИЕ!**\n👥 Группа: **{0}**\n{1}\n✅ Героев подтвердило: {2}/{3} ({4:P0})\n🎯 Требуется: {5:P0} героев + ВСЕ Мастера\nЗапускаю поиск нового времени... 🎲";
            public const string AutoPlanningCompleted = "🤖 **АВТО-ПЛАНИРОВАНИЕ ЗАВЕРШЕНО**\n✅ Избрано ближайшее окно: **{0}**\n👥 Героев в группе: **{1}**\nИгрокам отправлены свитки с приглашением. Как только 75% подтвердят — сессия будет назначена!";
            public const string RecommendationsTitle = "📊 **ВАРИАНТЫ ДЛЯ ГРУППЫ \"{0}\"**\nНайдено вариантов: {1}";
            public const string NoIntersectionsFound = "😔 **ПЕРЕСЕЧЕНИЙ НЕ НАЙДЕНО.**\nГерои заняты в разное время, звёзды не сошлись.\n*Рекомендации также недоступны.*";
            public const string NoRecommendationsFound = "😔 **АВТО-ПЛАНИРОВАНИЕ: {0}**\nУвы, общие окна не найдены и рекомендации недоступны.\n💡 Попробуйте:\n• Попросить героев добавить больше вариантов\n• Уменьшить длительность сессии";
            public const string PlanningError = "❌ **ОШИБКА АВТО-ПЛАНИРОВАНИЯ: {0}**\nНепредвиденная магия помешала поиску рекомендаций.\nДетали: {1}";
            public const string GroupMembersTitle = "👥 **СОСТАВ ОТРЯДА: {0}**\n\n📋 **Герои ({1}):**\n{2}\n\n🎯 **Мастера ({3}):**\n{4}";
            public const string NoAdminsInGroup = "ℹ️ В группе нет Мастеров";
            public const string SessionInfoTitle = "📅 **ИНФОРМАЦИЯ О СЕССИИ: {0}**\n\n🗓️ **Дата и время:** {1}\n🕒 **Длительность:** {2} ч.\n📊 **Статус:** {3}\n\n✅ **Подтвердили ({4}/{5}):**\n{6}\n\n❌ **Не смогут ({7}/{8}):**\n{9}";
            public const string NoSessionScheduled = "ℹ️ **СЕССИЯ НЕ ЗАПЛАНИРОВАНА**\nДля группы **{0}** ещё не назначено время приключений.\nИспользуйте команду /request, чтобы собрать расписание героев.";
        }
        /// <summary>
        /// Сообщения, отображаемые обычным игрокам.
        /// </summary>
        public static class PlayerMessages
        {
            public const string WelcomePlayer = "🛡 **Приветствую, Искатель Приключений!**\nЯ — твой проводник в мире планирования сессий.\nВместе мы соберём твою группу для следующего эпического приключения!";
            public const string WelcomeAdmin = "🧙 **Приветствую, Великий Мастер Подземелий!**\nЯ — твой верный магический помощник.\nПозволь мне взять на себя заботы о планировании, пока ты готовишь приключения!";
            public const string TimeZoneSet = "✅ **Твой часовой пояс установлен: UTC {0}**\nТеперь все времена будут отображаться правильно!";
            public const string TimeZoneError = "⚠️ Ошибка при обновлении настроек часового пояса";
            public const string TimeZoneFormatError = "⚠️ Неверный формат часового пояса. Укажи смещение как +3 или -5";
            public const string ActionCancelled = "🚫 Действие отменено. Возвращаемся к началу пути.";
            public const string DataSaved = "✅ Данные сохранены в магических кристаллах!";
            public const string AlreadyInGroup = "ℹ️ Ты уже состоишь в группе **{0}**";
            public const string NotInGroup = "ℹ️ Ты не состоишь в этой группе";
            public const string JoinedGroup = "⚔️ **ТЫ ВСТУПИЛ В ГРУППУ \"{0}\"!**\nДобро пожаловать в отряд, герой!";
            public const string LeftGroup = "🚪 **ТЫ ПОКИНУЛ ГРУППУ \"{0}\".**\nТвой путь с этим отрядом завершён.";
            public const string RsvpConfirmed = "✅ Ты подтвердил своё участие. Увидимся за столом!";
            public const string RsvpDeclined = "❌ Ты отказался от участия. Жаль, но мы поймём!";
            public const string RsvpError = "⚠️ Ошибка: данные не найдены";
            public const string RsvpStatusFixed = "ℹ️ Статус сессии уже определён и не может быть изменён";
            public const string CalendarTitle = "📅 **ТВОЙ ЛИЧНЫЙ ГРИМУАР ВРЕМЕНИ**\nВыбери даты и отметь часы, когда ты свободен для приключений:";
            public const string TimeSelectionTitle = "🕒 **ВЫБЕРИ ВРЕМЯ ДЛЯ {0}**\nОтметь часы, когда готов к игре:";
            public const string CalendarSentToPM = "📩 {0}, я отправил тебе личный гримуар в сообщения!";
            public const string CalendarPmFailed = "❌ {0}, я не могу написать тебе. Пожалуйста, начни со мной диалог в личных сообщениях.";
            public const string FreeTimeRequest = "🎲 **ЗАПРОС СВОБОДНОГО ВРЕМЕНИ**\n\nМастер запрашивает твоё расписание для планирования следующей сессии группы **{0}**.\n\n👉 **Пожалуйста, укажи когда ты свободен:**\n\n📝 **Инструкция:**\n1. Нажми /free или введи эту команду в чат с ботом\n2. Выбери удобные даты в календаре\n3. Отметь часы, когда доступен для игры\n4. Подтверди выбор кнопкой «✅ ЗАВЕРШИТЬ ЗАПОЛНЕНИЕ»\n\n⏰ Чем быстрее ты заполнишь расписание, тем скорее начнётся приключение!";
            public const string SessionAnnouncement = "⚔️ **ОБЪЯВЛЕН СБОР НА ПАРТИЮ!** ⚔️\n\n👥 Группа: **{0}**\n📅 Дата: **{1}**\n🕒 Начало: **{2}** (по МСК)\n\n🎲 Герои, подтвердите явку кнопками ниже!\nПусть кубы будут благосклонны к вам!";
            public const string SessionAnnouncementWithPlayers = "⚔️ **ОБЪЯВЛЕН СБОР НА ПАРТИЮ!** ⚔️\n\n👥 Группа: **{0}**\n📅 Дата: **{1}**\n🕒 Начало: **{2}** (по МСК)\n\n📋 **Участники отряда:**\n{3}\n\nИгроки, подтвердите явку кнопками ниже!";
            public const string AutoSessionAnnouncement = "⚔️ **АВТО-НАЗНАЧЕНИЕ СЕССИИ** ⚔️\n\n🤖 Бот-помощник подобрал оптимальное время на основе вашего расписания.\n\n👥 Группа: **{0}**\n📅 Дата: **{1}**\n🕒 Начало: **{2}** (по МСК)\n⏳ Длительность: **{3} ч.**\n\n❗ Пожалуйста, подтвердите явку кнопками ниже!\n🎯 Для подтверждения сессии требуется **75%** героев.";
            public const string AutoSessionAnnouncementPM = "⚔️ **НАПОМИНАНИЕ: АВТО-НАЗНАЧЕНИЕ СЕССИИ** ⚔️\n\n🤖 Бот-помощник подобрал оптимальное время на основе вашего расписания.\n\n👥 Группа: **{0}**\n📅 Дата: **{1}** (ваше время: {2})\n🕒 Начало: **{3}** (по МСК)\n⏳ Длительность: **{4} ч.**\n\n❗ Пожалуйста, подтвердите явку кнопками ниже!\n🎯 Для подтверждения сессии требуется **75%** героев.";
            public const string RecommendedSessionAnnouncement = "⚔️ **РЕКОМЕНДОВАННОЕ ВРЕМЯ** ⚔️\n\n🤖 Бот подобрал оптимальное время с учётом доступности.\n\n👥 Группа: **{0}**\n📅 Дата: **{1}**\n🕒 Начало: **{2}** (по МСК)\n⏳ Длительность: **{3} ч.**\n\n✅ **Свободны ({4}/{5}):**\n{6}\n\n❗ Пожалуйста, подтвердите явку кнопками ниже!";
            public const string SelectedRecommendationAnnouncement = "⚔️ **ВЫБРАН ВАРИАНТ #{0}** ⚔️\n\n👥 Группа: **{1}**\n📅 Дата: **{2}**\n🕒 Начало: **{3}** (по МСК)\n📊 {4}\n\nИгроки, подтвердите явку кнопками ниже!";
            public const string SelectedRecommendationAnnouncementPM = "⚔️ **ВЫБРАН ВАРИАНТ #{0}** ⚔️\n\n👥 Группа: **{1}**\n📅 Дата: **{2}** (ваше время: {3})\n🕒 Начало: **{4}** (по МСК)\n📊 {5}\n\nПожалуйста, подтвердите явку кнопками ниже!";
            public const string SessionConfirmedPM = "🎉 **СЕССИЯ ПОДТВЕРЖДЕНА!** 🎉\n\n👥 Группа: **{0}**\n📅 Дата: **{1}** (ваше время: {2})\n🕒 Начало: **{3}** (по МСК)\n\n✅ Вы подтвердили участие.\nГотовьте кубы и листы персонажей!\nЖдём вас в назначенное время! ⚔️";
            public const string SessionStillValid = "📅 **СЕССИЯ ОСТАЁТСЯ В СИЛЕ!**\n\n👥 Группа: **{0}**\n📅 Дата: **{1}** (МСК)\n✅ Могут присутствовать: {2}/{3} ({4:P0})\n🎯 Требуется: {5:P0}\n{6}\n\nВремя игры не изменилось! Готовьтесь к приключению!";
            public const string PlayerCannotAttendWarning = "⚠️ **ВНИМАНИЕ!**\n\nВы обновили расписание и больше не можете присутствовать на сессии группы **{0}**.\n\n📅 Дата: **{1}** (МСК)\n\n✅ Однако сессия остаётся в силе, так как набралось достаточно героев ({2:P0}).\n\nЕсли вы всё же планируете быть — пожалуйста, обновите своё расписание.";
            public const string NewPlanningRequired = "⚠️ **ТРЕБУЕТСЯ НОВОЕ ПЛАНИРОВАНИЕ!**\n\n👥 Группа: **{0}**\n{1}\n🎯 Требуется: {2:P0} героев + ВСЕ Мастера\n\nЗапускаю поиск нового времени... 🎲";
            public const string RequestSentToGroup = "✅ **ЗАПРОС ОТПРАВЛЕН!**\n\n📬 Уведомление отправлено в чат группы\n👥 Группа: **{0}**\n🔄 Данные голосования сброшены.\n\nКак только все герои нажмут «Завершить заполнение», запустится авто-планирование.";
            public const string RequestSentCallbackResponse = "Запрос отправлен в чат группы {0}";
            public const string GroupMembersList = "👥 **УЧАСТНИКИ ГРУППЫ: {0}**\n\n📋 **Герои ({1}):**\n{2}";
            public const string SessionInfoPlayer = "📅 **ТВОЯ СЛЕДУЮЩАЯ СЕССИЯ: {0}**\n\n🗓️ **Дата и время:** {1} (ваше время: {2})\n🕒 **Начало по МСК:** {3}\n📊 **Статус:** {4}\n\n✅ **Ты:** {5}";
            public const string NoSessionForPlayer = "ℹ️ **У ТЕБЯ НЕТ ЗАПЛАНИРОВАННЫХ СЕССИЙ**\nТы не состоишь ни в одной группе с назначенной сессией.\nИспользуй /join, чтобы вступить в группу и отправиться в приключение!";
            public const string YouAreConfirmed = "✅ Подтвердил участие";
            public const string YouAreDeclined = "❌ Не сможешь присутствовать";
            public const string YouHaveNotResponded = "⏳ Ещё не ответил";
            public const string HelpTitle = "❓ **ГРИМУАР ЗНАНИЙ (СПРАВКА)**";
            public const string HelpText = "🛡 **Приветствую, Искатель Приключений!**\n\nЯ — твой проводник в мире планирования сессий D&D.\nПозволь мне помочь тебе и твоей группе собраться для следующего эпического приключения!\n\n**📜 ДОСТУПНЫЕ КОМАНДЫ:**\n\n📅 **/free** — Открыть личный календарь и отметить свободные часы\n🌍 **/timezone** — Настроить свой часовой пояс\n👥 **/join** — Вступить в группу (вызывать в чате группы)\n📊 **/status** — Проверить статус планирования\n👥 **/members** — Показать участников группы\n📅 **/session** — Информация о следующей сессии\n❓ **/help** — Показать эту справку\n\n**🧙 КОМАНДЫ МАСТЕРА:**\n\n/group — Создать новую группу\n/delgroup — Удалить группу\n/request — Запросить у игроков свободное время\n/plan — Найти идеальное время для игры\n/recommendations — Показать рекомендации\n/cancel — Отменить активную сессию\n\n**⚠️ ВАЖНО:**\nДля подтверждения сессии требуется **75% игроков + ВСЕ администраторы**\n\nПусть ваши кубы всегда падают критическим успехом! 🎲✨";
        }
        /// <summary>
        /// Системные уведомления в основной чат.
        /// </summary>
        public static class SystemNotifications
        {
            public const string Prefix = "🔔 ";
            public const string PlayerFinishedVoting = "🔔 **@{0}** завершил заполнение своего гримуара времени!";
            public const string PlayerJoinedGroup = "⚔️ Герой **@{0}** вступил в группу **{1}**!";
            public const string GroupChanged = "⚠️ **СОСТАВ ГРУППЫ ИЗМЕНИЛСЯ**\nИгрок **@{0}** присоединился к группе **{1}**.\n\n🔄 Голосование сброшено — всем героям нужно заново заполнить расписание.";
            public const string TimeAssigned = "🎯 **ВРЕМЯ ИГРЫ НАЗНАЧЕНО!**\n\nБот-помощник автоматически подобрал ближайшее окно: **{0}**\n\nИгроки, проверьте личные сообщения от бота и подтвердите участие! ⚔️🎲";
            public const string NoTimeFound = "😔 **Группа {0}**: не найдено подходящего времени\nИгрокам будет отправлено уведомление с рекомендациями.";
            public const string FreeTimeRequested = "🔔 Мастер запросил свободное время для группы **{0}**. {1}";
            public const string FreeTimeRequestedWithCancel = "🔔 Мастер запросил свободное время для группы **{0}**.\n\n⚠️ Предыдущая сессия отменена — требуется новое планирование!";
            public const string SessionConfirmedNotification = "🎉 **СЕССИЯ ПОДТВЕРЖДЕНА ДЛЯ ГРУППЫ {0}!**\n📅 {1}\n\nВсе игроки получат свитки с приглашением в личные сообщения.";
            public const string SessionReminder = "⏰ **НАПОМИНАНИЕ О СЕССИИ**\n\nГруппа: **{0}**\nВремя: **{1}**\n\nНе забудьте подтвердить участие! ⚔️";
        }
        /// <summary>
        /// Тексты кнопок и элементов интерфейса.
        /// </summary>
        public static class UiTexts
        {
            public const string ButtonFinishVoting = "✅ ЗАВЕРШИТЬ ЗАПОЛНЕНИЕ";
            public const string ButtonBackToDates = "⬅️ Назад к датам";
            public const string ButtonCancel = "❌ Отмена";
            public const string ButtonRsvpYes = "⚔️ ИДУ";
            public const string ButtonRsvpNo = "🚫 НЕ СМОГУ";
            public const string ButtonRetryRequest = "🔁 Повторить запрос";
            public const string ButtonViewMembers = "👥 Участники";
            public const string ButtonViewSession = "📅 Инфо о сессии";
            public const string ButtonFreeTime = "📝 Моё время";
            public const string ButtonTimeZone = "🌍 Часовой пояс";
            public const string ButtonStatus = "📊 Статус";
            public const string ButtonHelp = "❓ Помощь";
            public const string CalendarDisplayFormat = "dd.MM (ddd)";
            public const string CalendarCallbackFormat = "yyyy-MM-dd";
            public const string TimeButtonFormat = "{0:D2}:00";
            public const string TimeButtonSelectedFormat = "✅ {0:D2}:00";
            public const string TimeButtonUnselectedFormat = "⬜️ {0:D2}:00";
        }
        /// <summary>
        /// Шаблоны команд и их описания.
        /// </summary>
        public static class Commands
        {
            public const string Start = "/start";
            public const string Group = "/group";
            public const string DeleteGroup = "/delgroup";
            public const string Join = "/join";
            public const string Leave = "/leave";
            public const string TimeZone = "/timezone";
            public const string Free = "/free";
            public const string Plan = "/plan";
            public const string Request = "/request";
            public const string Status = "/status";
            public const string Recommendations = "/recommendations";
            public const string Cancel = "/cancel";
            public const string Members = "/members";
            public const string SessionInfo = "/session";
            public const string Help = "/help";
            public const string CommandsList = "\n**📜 ДОСТУПНЫЕ КОМАНДЫ:**\n📅 /free — Отметить свое свободное время (в личке)\n🌍 /timezone — Настроить свой часовой пояс\n👥 /join — Вступить в группу (вызывать в чате группы)\n📊 /status — Проверить статус планирования\n👥 /members — Показать участников группы\n📅 /session — Информация о следующей сессии\n❓ /help — Показать справку";
            public const string AdminCommandsList = "\n**🧙 КОМАНДЫ МАСТЕРА:**\n/group — Создать новую группу\n/delgroup — Удалить группу\n/request — Запросить у игроков свободное время\n/plan — Найти идеальное время для игры\n/recommendations — Показать рекомендации (если нет пересечений)\n/cancel — Отменить активную сессию планирования";
            public const string ImportantNote = "\n**⚠️ ВАЖНО:** Для подтверждения сессии требуется 75% игроков + ВСЕ администраторы";
            public const string InDevelopment = "\n**🔮 В РАЗРАБОТКЕ:**\n⏳ _Авто-напоминания за 5ч и 1ч до игры_\n📊 _Статус заполнения времени группой_";
            public const string MembersPrompt = "👥 **ПРОСМОТР УЧАСТНИКОВ**\nВыберите группу, чтобы увидеть её состав:";
            public const string NoGroupsForMembers = "❌ Групп не найдено.";
            public const string SessionInfoPrompt = "📅 **ИНФОРМАЦИЯ О СЕССИИ**\nВыберите группу:";
            public const string NoGroupsForSessionInfo = "❌ У вас нет групп с запланированными сессиями.";
        }
        /// <summary>
        /// Сообщения об ошибках и предупреждения.
        /// </summary>
        public static class ErrorMessages
        {
            public const string GroupNotFound = "⚠️ Группа не найдена";
            public const string PlayerNotFound = "⚠️ Герой не найден";
            public const string DateParseError = "⚠️ Ошибка формата даты";
            public const string TimeParseError = "⚠️ Ошибка парсинга даты сессии из callback: {0}";
            public const string CallbackFormatError = "⚠️ Ошибка обработки";
            public const string CallbackDataMissing = "⚠️ Ошибка обработки запроса";
            public const string RecommendationUnavailable = "⚠️ Недоступные варианты рекомендаций";
            public const string GenericError = "⚠️ Произошла ошибка при обработке действия";
            public const string TimeZoneFormatError = "⚠️ Произошла ошибка при форматировании часового пояса";
            public const string TimeZoneError = "⚠️ Произошла ошибка с часовым поясом";
            public const string RsvpError = "⚠️ Произошла ошибка при ответе";
        }
        #endregion
        #region Сообщения команд (CommandHandler)
        /// <summary>
        /// Сообщения, используемые в обработчике команд.
        /// </summary>
        public static class CommandMessages
        {
            // Создание группы
            public const string CreateGroupPrompt = "📝 **СОЗДАНИЕ НОВОЙ ГРУППЫ**\n\nВведите название для вашей D&D кампании:\n(Например: \"Проклятие Страда\", \"Восхождение Драконьего Копья\")";
            public const string GroupNameEmptyError = "⚠️ Название не может быть пустым. Введите ещё раз:";
            public const string NoGroupsToDelete = "ℹ️ Групп для удаления пока нет.";
            public const string DeleteGroupPrompt = "⚠️ **УДАЛЕНИЕ ГРУППЫ**\n\nВыберите группу, которую хотите расформировать:\n(Это действие необратимо!)";
            // Вступление в группу
            public const string NoGroupsToJoin = "❌ Групп пока нет. Мастер должен создать их через /group";
            public const string JoinGroupPrompt = "📜 **ВЫБЕРИТЕ ГРУППУ ДЛЯ ВСТУПЛЕНИЯ**\n\nК какому отряду ты хочешь присоединиться?";
            // Выход из группы
            public const string NotInAnyGroup = "🛡 Ты пока не состоишь ни в одной группе.";
            public const string LeaveGroupPrompt = "🏃 **ВЫХОД ИЗ ГРУППЫ**\n\nВыберите группу, которую хотите покинуть:";
            // Часовой пояс
            public const string TimeZonePrompt = "🌍 **НАСТРОЙКА ЧАСОВОГО ПОЯСА**\n\nВыберите ваше смещение относительно UTC:\n(Например, для Москвы это +3, для Нью-Йорка -5)";
            // Свободное время
            public const string FreeTimePrompt = "📅 **ТВОЙ ЛИЧНЫЙ ГРИМУАР ВРЕМЕНИ**\n\nВыберите дату, чтобы отметить свободные часы:";
            public const string FreeTimeSentToPM = "📩 {0}, я отправил тебе личный гримуар в сообщения!";
            public const string FreeTimePmFailed = "❌ {0}, я не могу написать тебе. Пожалуйста, начни со мной диалог в личных сообщениях.";
            // Запрос свободного времени
            public const string NoGroupsForRequest = "❌ Сначала создайте группу через /group";
            public const string RequestFreeTimePrompt = "🎯 **ЗАПРОС СВОБОДНОГО ВРЕМЕНИ**\n\nВыберите группу, для которой нужно собрать расписание героев:";
            // Планирование
            public const string NoGroupsForPlan = "❌ Сначала создайте группу через /group";
            public const string PlanPrompt = "🎯 **ЗАПУСК ПЛАНИРОВАНИЯ**\n\nВыберите группу, для которой нужно найти идеальное время для приключения:";
            // Статус
            public const string NoGroupsForStatus = "📋 Вы не состоите ни в одной группе.";
            public const string StatusTitle = "📊 **ТВОЙ СТАТУС В ГРУППАХ:**\n";
            public const string StatusSessionLine = "   📅 Сессия: {0}";
            public const string StatusStatusLine = "   ✅ Статус: {0}";
            public const string StatusConfirmedLine = "   👍 Подтвердили: {0}/{1}";
            public const string StatusWaitingLine = "   ⏳ Ожидание планирования";
            public const string StatusVotingLine = "   📝 Заполнили расписание: {0}/{1}";
            // Рекомендации
            public const string AdminOnlyRecommendations = "🔒 Только Мастер может запрашивать рекомендации.";
            public const string NoGroupsForRecommendations = "❌ Групп не найдено.";
            public const string RecommendationsPrompt = "📊 **ПОЛУЧИТЬ РЕКОМЕНДАЦИИ**\n\nВыберите группу:";
            // Отмена сессии
            public const string AdminOnlyCancel = "🔒 Только Мастер может отменять сессии.";
            public const string NoActiveSessions = "ℹ️ Нет активных сессий для отмены.";
            public const string CancelSessionPrompt = "⚠️ **ОТМЕНА СЕССИИ**\n\nВыберите группу для отмены:";
            // Участники группы
            public const string AdminOnlyViewMembers = "🔒 Только Мастер может просматривать состав группы.";
            public const string NoGroupsForMembers = "❌ Групп не найдено.";
            public const string MembersPrompt = "👥 **ПРОСМОТР УЧАСТНИКОВ**\n\nВыберите группу:";
            // Информация о сессии
            public const string NoGroupsForSessionInfo = "❌ У вас нет групп с запланированными сессиями.";
            public const string SessionInfoPrompt = "📅 **ИНФОРМАЦИЯ О СЕССИИ**\n\nВыберите группу:";
        }
        #endregion
        #region Сообщения обработчика callback-запросов (CallbackHandler)
        /// <summary>
        /// Сообщения для логирования в CallbackHandler.
        /// </summary>
        public static class CallbackHandlerLogs
        {
            public const string CallbackNullData = "Получен CallbackQuery без данных от пользователя {UserId}";
            public const string CallbackProcessingLog = "Обработка callback '{Data}' от пользователя {UserId}";
            public const string CallbackCancelled = "Обработка callback прервана из-за отмены операции";
            public const string CallbackError = "Необработанное исключение при обработке callback '{Data}' от пользователя {UserId}";
            public const string CallbackRouteNullData = "Попытка маршрутизации callback без данных от пользователя {UserId}";
            public const string AdminActionAttempt = "Пользователь {UserId} попытался выполнить админ-действие: {Data}";
            public const string PlayerNotFoundCallback = "Игрок с TelegramId {UserId} не найден при обработке callback. Действие: {Data}";
            public const string UnknownCallback = "Неизвестный callback-запрос: {Data}";
            public const string RsvpInvalidGroupId = "Неверный ID группы в RSVP callback: {Data}";
            public const string RsvpStatusLog = "RSVP Группа {GroupId}: Подтвердили {ConfirmedCount}/{TotalPlayers} ({ParticipationRate:P1}). Ответили {RespondedCount}/{TotalPlayers}";
            public const string AllPlayersResponded = "Все игроки ответили, запускаем финализацию";
            public const string SessionConfirmedLog = "✅ Сессия группы {GroupName} подтверждена ({Rate:P1})";
            public const string SessionRescheduledLog = "⚠️ Сессия группы {GroupName} перепланирована — админы не могут присутствовать";
            public const string SessionCancelledLog = "❌ Сессия группы {GroupName} отменена ({Rate:P1})";
            public const string RecommendationInvalidFormat = "Неверный формат callback выбора рекомендации: {Data}";
            public const string AutoPlanFailed = "Авто-планирование не удалось для группы {GroupId}: {Message}";
            public const string AutoSlotSelected = "✅ Авто-выбор времени для {GroupName}: {StartTime} UTC";
            public const string RsvpSentDebug = "RSVP отправлен игроку {PlayerId}";
            public const string RsvpSendFailed = "Не удалось отправить RSVP игроку {PlayerId}";
            public const string AutoPlanResultsSent = "✅ Результаты авто-планирования отправлены для группы {GroupName}";
            public const string RecommendationsSent = "Рекомендации успешно отправлены для группы {GroupName}";
            public const string TimeZoneInvalidFormat = "Неверный формат часового пояса в callback: {Data}";
            public const string TimeZoneSetLog = "Пользователь {UserId} установил часовой пояс: UTC{Offset}";
            public const string DateParseFailed = "Не удалось распарсить дату из callback: {Data}";
            public const string ToggleTimeInvalidFormat = "Неверный формат toggle_time callback: {Data}";
            public const string SlotRemoved = "Удалён слот для пользователя {UserId} на {SlotTimeUtc}";
            public const string SlotAdded = "Добавлен слот для пользователя {UserId} на {SlotTimeUtc}";
            public const string VotingSaved = "Сохранено {Count} групп с обновлённым статусом голосования";
            public const string AllVotingFinished = "Все игроки ({Count}) завершили голосование для {GroupName}";
            public const string WaitingForVoting = "⏳ Ожидаем завершения голосования: {Finished}/{Total} для группы {GroupName}";
            public const string GroupHasSession = "Группа {GroupName} имеет назначенную сессию на {SessionTime}. Запуск проверки доступности...";
            public const string GroupNoSession = "Группа {GroupName} не имеет сессии. Запуск авто-планирования...";
            public const string GroupNoAssignedSession = "Группа {GroupName} не имеет назначенной сессии";
            public const string SessionConfirmedRate = "✅ Сессия {GroupName} подтверждена ({Rate:P1}). Не смогут: {CannotAttendCount}";
            public const string SessionRescheduledReason = "⚠️ Сессия {GroupName} перепланирована. Причина: {Reason}";
            public const string PlayerNotFoundJoin = "Не удалось найти игрока с TelegramId {UserId} при попытке вступить в группу";
            public const string PlayerJoinedGroupLog = "Пользователь {UserId} вступил в группу {GroupId} [{GroupName}]";
            public const string GroupVotingReset = "Группа {GroupName}: голосование сброшено из-за присоединения нового игрока {UserId}";
            public const string AdminDeletedGroup = "Админ {AdminId} удалил группу {GroupId} [{GroupName}]";
            public const string PlayerLeftGroup = "Пользователь {UserId} покинул группу {GroupId} [{GroupName}]";
            public const string FreeTimeRequestInvalidFormat = "Неверный формат ID группы в запросе свободного времени: {Data}";
            public const string FreeTimeRequestSent = "Запрос свободного времени отправлен в чат группы {GroupName}";
            public const string VotingDataReset = "Группа {GroupName}: сброшены данные голосования (SessionUtc={SessionUtc}, FinishedVoting={FinishedCount}, HadSession={HadSession})";
            public const string PlanningStart = "Запуск поиска окон для группы {GroupId}, мин. длительность: {Hours}ч";
            public const string GroupNotFoundCallback = "Игрок с TelegramId {UserId} не найден при обработке callback. Действие: {Data}";
            public const string MembersViewed = "Пользователь {UserId} просмотрел состав группы {GroupName}";
            public const string SessionInfoViewed = "Пользователь {UserId} просмотрел информацию о сессии группы {GroupName}";
            public const string StartMenuFreeTime = "Пользователь {UserId} открыл меню свободного времени из /start";
            public const string StartMenuTimeZone = "Пользователь {UserId} открыл меню часового пояса из /start";
            public const string StartMenuStatus = "Пользователь {UserId} открыл меню статуса из /start";
            public const string StartMenuJoin = "Пользователь {UserId} открыл меню вступления в группу из /start";
            public const string StartMenuPlan = "Пользователь {UserId} открыл меню планирования из /start";
            public const string StartMenuHelp = "Пользователь {UserId} запросил помощь из /start";
            public const string HelpMenuShown = "Пользователь {UserId} запросил справку";
            public const string ActionCompletedShowHelp = "Пользователь {UserId} завершил действие, показано меню помощи";
        }
        /// <summary>
        /// Сообщения для пользователей в CallbackHandler.
        /// </summary>
        public static class CallbackHandlerMessages
        {
            public const string SessionConfirmationText = "✅ Сессия для **{0}** назначена на {1}";
            public const string RecommendationSelectionText = "✅ Выбран вариант #{0}: {1}";
            public const string RecommendationsAdminText = "📊 **Рекомендации для {0}**\n✅ Выбран лучший вариант: **{1}**\n👥 Участвуют: {2}/{3}\n✅ Свободны: {4}";
            public const string SessionCancelledAdminText = "⚠️ **СЕССИЯ ОТМЕНЕНА**\n👥 Группа: **{0}**\n✅ Подтвердили: {1}/{2} ({3:P0})\n🎯 Требуется: {4:P0}\n\nЗапустить повторный запрос свободного времени?";
            public const string PlanningResultsTitle = "🗓 **НАЙДЕННЫЕ ОКНА (Ваше время):**\n";
            public const string PlanningResultLine = "🔹 {0}\n";
            public const string RecommendationOptionLine = "#{0}. 🕒 {1}\n👥 {2}/{3} игроков\n✅ Свободны: {4}\n";
            public const string AdminsCannotAttend = "❌ **Администраторы не могут:** {0}";
            public const string AllAdminsCanAttend = "✅ Все администраторы могут присутствовать";
            public const string NoAdminsInGroup = "ℹ️ В группе нет администраторов";
            public const string AllPlayersCanAttend = "✅ Все игроки могут присутствовать!";
            public const string CannotAttendPlayers = "⚠️ **Не смогут присутствовать:** {0}";
            public const string RescheduleReasonAdmins = "админы не могут";
            public const string RescheduleReasonPlayers = "мало игроков";
            public const string NoData = "Нет данных";
            public const string UnknownGroup = "Неизвестно";
            public const string NoPlayersInGroup = "ℹ️ В группе пока нет игроков";
            public const string SessionNotScheduled = "ℹ️ Сессия ещё не запланирована";
        }
        #endregion
        #region Системные сообщения и логи
        /// <summary>
        /// Сообщения для логирования и системных уведомлений.
        /// </summary>
        public static class SystemMessages
        {
            // Program.cs
            public const string WarningMainChatIdNotConfigured = "⚠️ Предупреждение: '{0}:{1}' не настроен. Системные уведомления отключены.";
            public const string WarningAdminIdsNotConfigured = "⚠️ Предупреждение: '{0}:{1}' не настроен. Админ-команды будут недоступны.";
            public const string DirectoryCreated = "📁 Создана директория: {0}";
            public const string BotAuthenticated = "✅ Бот @{0} (ID: {1}) успешно аутентифицирован";
            public const string BotAuthFailed = "❌ Не удалось аутентифицировать бота. Проверьте токен и подключение к интернету";
            public const string TokenNotFound = "Обязательная настройка '{0}:{1}' не найдена. Добавьте токен бота в appsettings.json или переменные окружения.";
            public const string DatabaseInitializing = "Инициализация базы данных...";
            public const string DatabaseConnectFailed = "Не удалось подключиться к БД. Попытка создания...";
            public const string ApplyingMigrations = "Применение {0} миграций: {1}";
            public const string MigrationsApplied = "Миграции успешно применены";
            public const string DatabaseCreatedFromScratch = "База данных создана с нуля";
            public const string DatabaseSchemaActual = "Схема базы данных актуальна";
            public const string DatabaseInitError = "Критическая ошибка при инициализации базы данных";
            // BotBackgroundService.cs
            public const string BotStartingInit = "Запуск инициализации Telegram-бота...";
            public const string BotTokenCheckFailed = "Не удалось получить информацию о боте. Проверьте токен API.";
            public const string BotUpdateHandlersSetup = "Настройка обработчиков обновлений...";
            public const string BotStartedWaiting = "✅ Бот запущен и ожидает входящие события";
            public const string BotUpdateDelegated = "Делегирование обновления типа {0} в UpdateHandler";
            public const string BotUpdateCancelled = "Обработка обновления прервана из-за отмены службы";
            public const string BotUpdateError = "Необработанное исключение при обработке обновления типа {0}";
            public const string BotApiError = "API-ошибка Telegram [{0}]: {1}. Источник: {2}";
            public const string BotServiceCancelled = "Операция отменена при остановке службы. Источник: {0}";
            public const string BotCriticalError = "Критическая ошибка в конвейере получения обновлений. Источник: {0}";
            public const string BotStopping = "Получен сигнал остановки бота. Завершение работы...";
            public const string BotStopped = "Бот остановлен";
            // UpdateHandler.cs
            public const string UpdateReceivedNull = "Получено пустое обновление (null)";
            public const string UpdateReceived = "Получено обновление типа {0}, ChatId: {1}, UserId: {2}";
            public const string UpdateSkipped = "Пропущено обновление типа {0}: не поддерживается";
            public const string UpdateError = "Необработанное исключение при обработке обновления типа {0}";
            public const string MessageProcessing = "Обработка команды от пользователя {0} в чате {1}: {2}";
            public const string CallbackProcessing = "Обработка callback от пользователя {0}: {1}";
            // BaseHandler.cs
            public const string MainChatIdParseFailed = "Не удалось распарсить MainChatId из конфигурации. Значение: {0}";
            public const string AdminIdParseFailed = "Не удалось распарсить AdminId из конфигурации. Значение: {0}";
            public const string AdminIdsNotConfigured = "AdminIds не настроен в конфигурации";
            public const string SendMessageToUser = "Отправка сообщения пользователю {0}: {1}";
            public const string SendMessageToGroup = "Отправка сообщения в чат группы {0}: {1}";
            public const string NotifyUser = "Уведомление отправлено пользователю @{0}: {1}";
            public const string NotifyUserFailed = "Не удалось отправить уведомление пользователю {0}";
            public const string SendMessageToMainAdmin = "Отправка сообщения главному администратору {0}: {1}";
            public const string MainAdminNotConfigured = "Попытка отправить сообщение главному администратору, но AdminIds не настроен";
            public const string NotifyAdmin = "Уведомление отправлено администратору @{0}: {1}";
            public const string NotifyAdminFailed = "Не удалось отправить уведомление администратору {0}";
            public const string NotifyMainChat = "Системное уведомление в основной чат: {0}";
            public const string MainChatNotConfigured = "Попытка отправить уведомление в MainChat, но MainChatId не настроен";
            public const string EditMessage = "Редактирование сообщения {0} в чате {1}";
            public const string EditMessageNull = "Попытка редактировать сообщение, но CallbackQuery.Message равен null";
            public const string EditMarkupNull = "Попытка редактировать клавиатуру, но CallbackQuery.Message равен null";
            // CommandHandler.cs
            public const string CommandNoText = "Получено сообщение без текста от пользователя {0}";
            public const string CommandProcessing = "Обработка команды '{0}' от пользователя {1} в чате {2}";
            public const string CommandUnknown = "Неизвестная команда '{0}' от пользователя {1}";
            public const string AdminNoPermissionCreate = "Пользователь {0} без прав попытался создать группу";
            public const string GroupCreatedLog = "Админ {0} создал группу '{1}' в чате {2}";
        }
        #endregion
        #region Форматы и утилиты
        /// <summary>
        /// Форматы дат и времени для отображения и передачи в callback.
        /// </summary>
        public static class DateFormats
        {
            public const string DisplayFormat = "dd.MM (ddd)";
            public const string CallbackFormat = "yyyy-MM-dd";
            public const string DateTimeCallbackFormat = "yyyyMMddHH";
            public const string LocalTimeFormat = "dd.MM HH:mm";
            public const string FullLocalTimeFormat = "dd.MM (ddd) HH:mm";
        }
        /// <summary>
        /// Методы для работы с текстом и экранирования.
        /// </summary>
        public static class TextHelpers
        {
            /// <summary>
            /// Экранирует спецсимволы Markdown v2 для безопасной передачи в Telegram API.
            /// </summary>
            public static string EscapeMarkdown(string text)
            {
                if (string.IsNullOrEmpty(text))
                    return text;
                return text.Replace("_", "\\_").Replace("*", "\\*").Replace("`", "\\`").Replace("[", "\\[");
            }
            /// <summary>
            /// Обрезает текст до указанной длины для безопасного логирования.
            /// </summary>
            public static string TruncateForLog(string? text, int maxLength = LogTruncateLength) =>
                string.IsNullOrEmpty(text)
                ? string.Empty
                : text.Length <= maxLength ? text : text[..maxLength] + "...";
            /// <summary>
            /// Форматирует смещение часового пояса для отображения (например, +3, -5).
            /// </summary>
            public static string FormatTimeZoneOffset(int offset) =>
                $"{(offset >= 0 ? "+" : "")}{offset}";
        }
        #endregion
    }
}