using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace MoonThemedGame
{
    public enum GameLanguage { English, Russian }

    // Game-wide language. English text is the key; Russian comes from the table below.
    // Code calls T("...") / F("... {0}", args); UI Toolkit trees are stamped once and re-applied on change.
    public static class Loc
    {
        const string PrefsKey = "language";
        static GameLanguage? current;

        public static event Action Changed;

        public static GameLanguage Current
        {
            get
            {
                if (current == null)
                {
                    int fallback = Application.systemLanguage == SystemLanguage.Russian ? 1 : 0;
                    current = (GameLanguage)Mathf.Clamp(PlayerPrefs.GetInt(PrefsKey, fallback), 0, 1);
                }
                return current.Value;
            }
        }

        public static bool Russian => Current == GameLanguage.Russian;

        public static void Set(GameLanguage language)
        {
            if (Current == language) return;
            current = language;
            PlayerPrefs.SetInt(PrefsKey, (int)language);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        public static void Toggle() => Set(Russian ? GameLanguage.English : GameLanguage.Russian);

        public static string T(string english)
        {
            if (string.IsNullOrEmpty(english) || !Russian) return english;
            return Ru.TryGetValue(english, out var russian) ? russian : english;
        }

        public static string F(string english, params object[] args) => string.Format(T(english), args);

        sealed class Original
        {
            public string text;
        }

        // Remembers the English text of every label and button under root.
        public static void Stamp(VisualElement root)
        {
            if (root == null) return;
            root.Query<TextElement>().ForEach(element =>
            {
                if (element.userData == null) element.userData = new Original { text = element.text };
            });
        }

        public static void Apply(VisualElement root)
        {
            if (root == null) return;
            root.Query<TextElement>().ForEach(element =>
            {
                if (element.userData is Original original) element.text = T(original.text);
            });
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetState()
        {
            current = null;
            Changed = null;
        }

        static readonly Dictionary<string, string> Ru = new Dictionary<string, string>
        {
            // Settings menu and controls
            { "PALE HORSE /// STATION TERMINAL", "PALE HORSE /// ТЕРМИНАЛ СТАНЦИИ" },
            { "LUNAR TECHNICAL STATION 7   BUILD_RUV", "ЛУННАЯ ТЕХНИЧЕСКАЯ СТАНЦИЯ 7   BUILD_RUV" },
            { "SETTINGS", "НАСТРОЙКИ" },
            { "MASTER VOLUME", "ОБЩАЯ ГРОМКОСТЬ" },
            { "LANGUAGE: ENGLISH", "ЯЗЫК: РУССКИЙ" },
            { "CONTROLS  >", "УПРАВЛЕНИЕ  >" },
            { "RESUME", "ПРОДОЛЖИТЬ" },
            { "EXIT GAME", "ВЫЙТИ ИЗ ИГРЫ" },
            { "CONTROLS", "УПРАВЛЕНИЕ" },
            { "Select a key, then press its replacement.", "Выберите действие и нажмите новую клавишу." },
            { "RESTORE DEFAULTS", "ПО УМОЛЧАНИЮ" },
            { "Default keys restored.", "Клавиши по умолчанию восстановлены." },
            { "BACK", "НАЗАД" },
            { "EXIT TO DESKTOP?", "ВЫЙТИ НА РАБОЧИЙ СТОЛ?" },
            { "CANCEL", "ОТМЕНА" },
            { "[ESC] BACK    CHANGES SAVED AUTOMATICALLY", "[ESC] НАЗАД    ИЗМЕНЕНИЯ СОХРАНЯЮТСЯ АВТОМАТИЧЕСКИ" },
            { "PRESS KEY...", "НАЖМИТЕ КЛАВИШУ..." },
            { "Waiting: {0}. Press ESC to cancel.", "Ожидание: {0}. ESC — отмена." },
            { "Assignment cancelled.", "Назначение отменено." },
            { "ESC is reserved for back / cancel.", "ESC занят под «назад / отмена»." },
            { "Already assigned: {0}.", "Уже назначено: {0}." },
            { "Saved: {0}  [{1}]", "Сохранено: {0}  [{1}]" },
            { "Move forward", "Вперёд" },
            { "Move backward", "Назад" },
            { "Move left", "Влево" },
            { "Move right", "Вправо" },
            { "Interact / take", "Взаимодействие / взять" },
            { "Throw object", "Бросить предмет" },
            { "Repair robot", "Чинить робота" },
            { "Retro / ASCII", "Ретро / ASCII" },
            { "Previous station", "Предыдущая станция" },
            { "Next station", "Следующая станция" },
            { "Radio on", "Радио вкл" },
            { "Radio off", "Радио выкл" },
            { "Robot tuner", "Тюнер робота" },
            { "Satellite 1", "Спутник 1" },
            { "Satellite 2", "Спутник 2" },
            { "Satellite 3", "Спутник 3" },
            { "Open settings", "Открыть настройки" },
            { "Reset robot camera", "Вернуть камеру робота" },
            { "Robot handbrake", "Ручник робота" },
            { "Right overturned robot", "Перевернуть бота на колёса" },
            { "Crouch (hold)", "Присесть (удерживать)" },
            { "Jump (operator)", "Прыжок (человек)" },
            { "Sprint (hold)", "Бег (удерживать)" },
            { "[{0}] RIGHT THE ROBOT", "[{0}] ПОСТАВИТЬ БОТА НА КОЛЁСА" },
            { "ROBOT BACK ON WHEELS", "БОТ СНОВА НА КОЛЁСАХ" },

            // Interaction prompts
            { "[LMB] Eat", "[ЛКМ] Съесть" },
            { "Eating...", "Ем..." },
            { "[{0}] Take radio", "[{0}] Взять радио" },
            { "[{0} / {1}] radio11\n[{2}] Play   [{3}] Off", "[{0} / {1}] radio11\n[{2}] Включить   [{3}] Выключить" },
            { "[{0}] Throw", "[{0}] Бросить" },
            { "[{0}] Drop", "[{0}] Положить" },
            { "[{0}] Play   [{1}] Off", "[{0}] Включить   [{1}] Выключить" },
            { "[{0} / ESC] Put down", "[{0} / ESC] Положить" },
            { "[MMB HOLD] Tool belt", "[УДЕРЖ. СКМ] Пояс инструментов" },
            { "E — Interact", "E — Взаимодействовать" },
            { "E — Use computer", "E — Использовать компьютер" },
            { "E — Open", "E — Открыть" },
            { "E — Close", "E — Закрыть" },
            { "E — View weather", "E — Посмотреть погоду" },
            { "E — Read note", "E — Прочитать записку" },
            { "E — Take apple", "E — Взять яблоко" },
            { "Suspicious Apple", "Подозрительное яблоко" },
            { "[{0}] Dispense Suspicious Apple", "[{0}] Выдать подозрительное яблоко" },
            { "DISPENSING...", "ВЫДАЧА..." },
            { "Collect the apples first", "Сначала заберите яблоки" },
            { "Clear the tray", "Освободите лоток" },
            { "E — Take radio\n{0} / {1} — radio11   {2} — Play   {3} — Off", "E — Взять радио\n{0} / {1} — станция   {2} — Играть   {3} — Выкл" },
            { "{0} — Throw", "{0} — Бросить" },
            { "    {0} Play / {1} Off", "    {0} Играть / {1} Выкл" },
            { "{0} / ESC — Put down", "{0} / ESC — Положить" },
            { "Crane is busy", "Кран занят" },
            { "E — Call crane", "E — Вызвать кран" },
            { "Robot diagnostics…", "Диагностика робота…" },
            { "Robot repaired. You can call the crane.", "Робот исправен. Можно вызвать кран." },
            { "Robot repaired. Waiting for landing.", "Робот исправен. Ожидает приземления." },
            { "Missing parts:", "Не хватает деталей:" },

            // Paper note
            { "Login: {0}\nPassword: {1}\n\nDon't forget to change\nyour password after\nlogging in.", "Логин: {0}\nПароль: {1}\n\nНе забудь сменить\nпароль после\nвхода." },

            // PC login
            { "PALE HORSE // STATION TERMINAL", "PALE HORSE // ТЕРМИНАЛ СТАНЦИИ" },
            { "OPERATOR LOGIN", "ВХОД ОПЕРАТОРА" },
            { "USER", "ЛОГИН" },
            { "PASSWORD", "ПАРОЛЬ" },
            { "[ENTER] NEXT / LOG IN    [TAB] SWITCH FIELD", "[ENTER] ДАЛЕЕ / ВХОД    [TAB] СМЕНА ПОЛЯ" },
            { "USER REQUIRED", "ВВЕДИТЕ ЛОГИН" },
            { "PASSWORD REQUIRED", "ВВЕДИТЕ ПАРОЛЬ" },
            { "VERIFYING", "ПРОВЕРКА" },
            { "ACCESS GRANTED - WELCOME, {0}", "ДОСТУП РАЗРЕШЁН - ДОБРО ПОЖАЛОВАТЬ, {0}" },
            { "ACCESS DENIED - INVALID USER OR PASSWORD", "ДОСТУП ЗАПРЕЩЁН - НЕВЕРНЫЙ ЛОГИН ИЛИ ПАРОЛЬ" },

            // Weather
            { "FOG / MOON DUST", "ТУМАН / ЛУННАЯ ПЫЛЬ" },
            { "RADIATION", "РАДИАЦИЯ" },
            { "METEORITE RAIN", "МЕТЕОРИТНЫЙ ДОЖДЬ" },
            { "SUNNY", "ЯСНО" },
            { "Low Visibility", "Плохая видимость" },
            { "Drains your energy faster", "Энергия тратится быстрее" },
            { "Meteorites drop from the sky,\nwhen hit - drains your energy", "С неба падают метеориты,\nпопадание отнимает энергию" },
            { "Perfect Weather", "Отличная погода" },
            { "METEOR IMPACT - ENERGY LOST", "УДАР МЕТЕОРИТА - ПОТЕРЯ ЭНЕРГИИ" },
            { "WARN RADIATION RISING - cell drain growing", "ВНИМ РАДИАЦИЯ РАСТЁТ - расход батареи увеличивается" },
            { "RADIATION LEVEL NORMAL", "РАДИАЦИЯ В НОРМЕ" },
            { "RADIATION - CELL DRAIN x{0} - RETURN TO BASE", "РАДИАЦИЯ - РАСХОД БАТАРЕИ x{0} - ВЕРНИТЕСЬ НА БАЗУ" },
            { "CURRENT WEATHER", "ТЕКУЩАЯ ПОГОДА" },
            { "NEXT CONDITIONS", "ДАЛЕЕ" },
            { "CHANGES IN {0}", "СМЕНА ЧЕРЕЗ {0}" },
            { "IN {0}", "ЧЕРЕЗ {0}" },
            { "ENERGY DRAIN x{0}", "РАСХОД ЭНЕРГИИ x{0}" },
            { "IMPACT ZONES MARKED {0}s BEFORE", "ЗОНЫ УДАРА ОТМЕЧАЮТСЯ ЗА {0}с" },
            { "BASE INTERIOR / SHELTERED", "ВНУТРИ БАЗЫ / УКРЫТИЕ" },
            { "SURFACE FORECAST / T+{0}", "ПРОГНОЗ ПОВЕРХНОСТИ / T+{0}" },

            // Robot feed
            { "PING base.station ... ok", "ПИНГ база.станция ... ок" },
            { "SERVO BUS 4/4 ok", "ШИНА СЕРВО 4/4 ок" },
            { "IMU recal ... ok", "ИНС перекалибровка ... ок" },
            { "LENS heater on", "ПОДОГРЕВ ЛИНЗЫ вкл" },
            { "UPLINK frame resync", "КАНАЛ ресинхронизация кадра" },
            { "THERMAL loop nominal", "ТЕРМОКОНТУР в норме" },
            { "MEM 61% heap ok", "ПАМЯТЬ 61% куча ок" },
            { "GPS-L lock 7 sat", "ЛНС захват 7 спутн." },
            { "DUST on lens: 12%", "ПЫЛЬ на линзе: 12%" },
            { "WHEEL ENC drift 0.02", "ЭНКОДЕР КОЛЁС дрейф 0.02" },
            { "WATCHDOG kick", "СТОРОЖ сброшен" },
            { "CAM exposure auto", "КАМЕРА экспозиция авто" },
            { "WARN signal degraded", "ВНИМ сигнал ухудшен" },
            { "ERR packet crc", "ОШ контрольная сумма пакета" },
            { "WARN cell undervolt", "ВНИМ низкое напряжение батареи" },
            { "ERR frame dropped", "ОШ кадр потерян" },
            { "WARN brownout imminent", "ВНИМ скоро просадка питания" },
            { "ERR uplink timeout", "ОШ таймаут канала" },
            { "LINK ESTABLISHED  ch 0x3F2A", "СВЯЗЬ УСТАНОВЛЕНА  кан 0x3F2A" },
            { "FW {0}", "ПО {0}" },
            { "NONE", "НЕТ" },
            { "UPLINK via BASE RELAY", "КАНАЛ через РЕТРАНСЛЯТОР БАЗЫ" },
            { "UPLINK via {0}  {1}", "КАНАЛ через {0}  {1}" },
            { "ERR {0} out of range - uplink lost", "ОШ {0} вне зоны - канал потерян" },
            { "ALERT {0} carrier hijacked by {1}", "ТРЕВОГА несущая {0} перехвачена {1}" },
            { "RECEIVER RETUNE - carrier offline", "ПЕРЕСТРОЙКА ПРИЁМНИКА - несущей нет" },
            { "ERR no satellite selected", "ОШ спутник не выбран" },
            { "HANDOVER -> {0}", "ПЕРЕДАЧА -> {0}" },
            { "WARN rival carrier {0} jamming CH{1}", "ВНИМ чужая несущая {0} глушит КАН{1}" },
            { "WARN retune to a free channel [H]", "ВНИМ перестройтесь на свободный канал [H]" },
            { "CARRIER LOCKED CH{0} - {1} lost the signal", "НЕСУЩАЯ ЗАХВАЧЕНА КАН{0} - {1} потерял сигнал" },
            { "SIGNAL HIJACKED BY RIVAL SATELLITE", "СИГНАЛ ПЕРЕХВАЧЕН СПУТНИКОМ КОНКУРЕНТА" },
            { "RECEIVER RETUNING TO NEW CHANNEL", "ПРИЁМНИК ПЕРЕСТРАИВАЕТСЯ НА НОВЫЙ КАНАЛ" },
            { "SATELLITE OUT OF RANGE", "СПУТНИК ВНЕ ЗОНЫ" },
            { "NO SATELLITE SELECTED", "СПУТНИК НЕ ВЫБРАН" },
            { "DOCKED AT CHARGE STATION - CHARGING", "НА ЗАРЯДНОЙ СТАНЦИИ - ЗАРЯДКА" },
            { "CHARGE FULL", "ЗАРЯД ПОЛНЫЙ" },
            { "UNDOCKED FROM CHARGER", "ОТСТЫКОВКА ОТ ЗАРЯДКИ" },
            { "WARN overhead cover {0}% - signal degraded", "ВНИМ укрытие сверху {0}% - сигнал ухудшен" },
            { "SKY CLEAR - signal restored", "НЕБО ЧИСТОЕ - сигнал восстановлен" },
            { "WARN {0} signal weak", "ВНИМ слабый сигнал {0}" },
            { "WARN {0} leaves range in {1}s", "ВНИМ {0} уходит из зоны через {1}с" },
            { "DIAL SCAV_BOT  ch 0x3F2A", "ВЫЗОВ SCAV_BOT  кан 0x3F2A" },
            { "SAT UPLINK", "СПУТНИКОВЫЙ КАНАЛ" },
            { "HANDSHAKE", "РУКОПОЖАТИЕ" },
            { "AUTH OPERATOR", "АВТОРИЗАЦИЯ ОПЕРАТОРА" },
            { "VIDEO STREAM", "ВИДЕОПОТОК" },
            { "OK", "ОК" },
            { "FAIL", "СБОЙ" },
            { "NO CARRIER", "НЕТ НЕСУЩЕЙ" },
            { "ESTABLISHING CONNECTION", "УСТАНОВКА СОЕДИНЕНИЯ" },
            { "CONNECTION TERMINATED", "СОЕДИНЕНИЕ РАЗОРВАНО" },
            { "LINK CLOSED BY OPERATOR   SESSION {0:00}:{1:00}   PKT {2}", "СВЯЗЬ ЗАКРЫТА ОПЕРАТОРОМ   СЕАНС {0:00}:{1:00}   ПАК {2}" },
            { "UPLINK {0} RELEASED", "КАНАЛ {0} ОСВОБОЖДЁН" },
            { "{0} // LIVE FEED", "{0} // ПРЯМОЙ ЭФИР" },
            { "LINK 0x3F2A  PKT {0}  LOSS {1}%", "СВЯЗЬ 0x3F2A  ПАК {0}  ПОТЕРИ {1}%" },
            { "LAT {0}ms  RSSI {1}dBm", "ЗАДЕРЖКА {0}мс  RSSI {1}дБм" },
            { "POS {0} {1} {2}  HDG {3:000}", "ПОЗ {0} {1} {2}  КУРС {3:000}" },
            { "CELL {0}V  T {1}C  CARGO {2}/{3}{4}", "БАТ {0}В  T {1}C  ГРУЗ {2}/{3}{4}" },
            { "  LAMP HI", "  ФАРЫ ДАЛЬН" },
            { "UPTIME {0:00}:{1:00}:{2:00}", "В РАБОТЕ {0:00}:{1:00}:{2:00}" },
            { "CHARGE {0:000}%", "ЗАРЯД {0:000}%" },
            { "STATUS {0}", "СТАТУС {0}" },
            { "SIGNAL HIJACKED", "СИГНАЛ ПЕРЕХВАЧЕН" },
            { "RETUNING  {0}", "ПЕРЕСТРОЙКА  {0}" },
            { "NO UPLINK", "НЕТ КАНАЛА" },
            { "[H] TUNER - PLUG A FREE CHANNEL AND LOCK", "[H] ТЮНЕР - ВКЛЮЧИТЕ СВОБОДНЫЙ КАНАЛ И ЗАХВАТИТЕ" },
            { "RETUNING CARRIER...", "ПЕРЕСТРОЙКА НЕСУЩЕЙ..." },
            { "PRESS {0} TO SWITCH SATELLITE", "НАЖМИТЕ {0}, ЧТОБЫ СМЕНИТЬ СПУТНИК" },
            { "SIGNAL LOSS IN {0}", "ПОТЕРЯ СИГНАЛА ЧЕРЕЗ {0}" },
            { "ROBOT OFFLINE", "РОБОТ ОТКЛЮЧЁН" },
            { "[E] ORDER EVACUATION - {0} CR   BALANCE {1} CR", "[E] ЗАКАЗАТЬ ЭВАКУАЦИЮ - {0} CR   БАЛАНС {1} CR" },
            { "EVACUATION EN ROUTE  {0}", "ЭВАКУАЦИЯ В ПУТИ  {0}" },
            { "LEVER - MANIPULATOR ARM REQUIRED (SHOP)", "РЫЧАГ - НУЖНА РУКА-МАНИПУЛЯТОР (МАГАЗИН)" },
            { "ARM ENGAGED...", "РУКА РАБОТАЕТ..." },
            { "[E] PULL LEVER  ({0})", "[E] ДЁРНУТЬ РЫЧАГ  ({0})" },
            { "ON", "ВКЛ" },
            { "OFF", "ВЫКЛ" },
            { "LEVER - DRIVE CLOSER, ARM CAN'T REACH", "РЫЧАГ - ПОДЪЕДЬТЕ БЛИЖЕ, РУКА НЕ ДОСТАЁТ" },
            { "{0} - DRIVE   {1} - SATELLITE   [{2}] - TUNER   ESC - BACK TO PC", "{0} - ЕХАТЬ   {1} - СПУТНИК   [{2}] - ТЮНЕР   ESC - К ПК" },
            { "[{0}] HANDBRAKE   MOUSE / RIGHT STICK - LOOK   [{1}] / MMB - RECENTER", "[{0}] РУЧНИК   МЫШЬ / ПРАВЫЙ СТИК - ОБЗОР   [{1}] / СКМ - ВЕРНУТЬ КАМЕРУ" },
            { "[UP] FRONT FLIP   [DOWN] BACK FLIP   {0}/{1} - MOVE", "[UP] ФРОНТФЛИП   [DOWN] БЭКФЛИП   {0}/{1} - ПЕРЕМЕЩЕНИЕ" },
            { "  COVER {0}%", "  УКРЫТИЕ {0}%" },
            { "LINK BASE RELAY  Q {0}%{1}", "КАНАЛ РЕТРАНСЛЯТОР БАЗЫ  К {0}%{1}" },
            { "SAT NONE", "СПУТНИК НЕТ" },
            { "SAT {0}  Q {1}%  LEFT {2}{3}", "СПУТНИК {0}  К {1}%  ОСТ {2}{3}" },
            { "SAT {0}  OUT OF RANGE", "СПУТНИК {0}  ВНЕ ЗОНЫ" },
            { "SATELLITES", "СПУТНИКИ" },
            { "{0} left", "ост {0}" },
            { "back {0}", "через {0}" },
            { "RIVAL {0} INTERCEPTING {1}  {2}%  -  [H] TUNER", "КОНКУРЕНТ {0} ПЕРЕХВАТЫВАЕТ {1}  {2}%  -  [H] ТЮНЕР" },
            { "{0} LEAVES RANGE IN {1}s - SWITCH SATELLITE ({2})", "{0} УХОДИТ ИЗ ЗОНЫ ЧЕРЕЗ {1}с - СМЕНИТЕ СПУТНИК ({2})" },
            { "CHARGING", "ЗАРЯДКА" },
            { "LOW CHARGE", "НИЗКИЙ ЗАРЯД" },
            { "ONLINE", "В СЕТИ" },
            { "CRITICAL", "КРИТИЧНО" },
            { "OFFLINE", "ОТКЛЮЧЁН" },
            { "EVACUATING", "ЭВАКУАЦИЯ" },
            { "MANIPULATOR ARM REQUIRED - BUY IT IN SHOP", "НУЖНА РУКА-МАНИПУЛЯТОР - КУПИТЕ В МАГАЗИНЕ" },
            { "ARM CAN'T REACH - DRIVE CLOSER TO THE LEVER", "РУКА НЕ ДОСТАЁТ - ПОДЪЕДЬТЕ БЛИЖЕ К РЫЧАГУ" },
            { "ARM: PUSHING LEVER DOWN", "РУКА: ОПУСКАЕТ РЫЧАГ" },
            { "ARM: LIFTING LEVER", "РУКА: ПОДНИМАЕТ РЫЧАГ" },
            { "SIGNAL HIJACKED BY {0}  DATA STOLEN -{1} CR", "СИГНАЛ ПЕРЕХВАЧЕН {0}  УКРАДЕНЫ ДАННЫЕ -{1} CR" },
            { "EVACUATION ORDERED  -{0} CR", "ЭВАКУАЦИЯ ЗАКАЗАНА  -{0} CR" },
            { "NOT ENOUGH CREDITS  ({0}/{1} CR)", "НЕ ХВАТАЕТ КРЕДИТОВ  ({0}/{1} CR)" },

            // Tuner
            { "TUNER IS EMPTY", "ТЮНЕР ПУСТ" },
            { "CH{0} IS JAMMED", "КАН{0} ЗАГЛУШЁН" },
            { "ERR NO LOCK - {0}", "ОШ НЕТ ЗАХВАТА - {0}" },
            { "  +{0}% TO RIVAL", "  +{0}% КОНКУРЕНТУ" },
            { "CARRIER LOCKED CH{0} - {1} LOST THE SIGNAL", "НЕСУЩАЯ ЗАХВАЧЕНА КАН{0} - {1} ПОТЕРЯЛ СИГНАЛ" },
            { "CARRIER STABLE CH{0}", "НЕСУЩАЯ СТАБИЛЬНА КАН{0}" },
            { "RCVR-2 // CARRIER TUNER", "ПРМ-2 // ТЮНЕР НЕСУЩЕЙ" },
            { "[H] / [ESC] EXIT", "[H] / [ESC] ВЫХОД" },
            { "ALERT  {0} IS JAMMING THE CARRIER", "ТРЕВОГА  {0} ГЛУШИТ НЕСУЩУЮ" },
            { "INTERCEPT {0} {1,3}%", "ПЕРЕХВАТ {0} {1,3}%" },
            { "ALERT  SIGNAL HIJACKED BY {0}", "ТРЕВОГА  СИГНАЛ ПЕРЕХВАЧЕН {0}" },
            { "RETUNING...", "ПЕРЕСТРОЙКА..." },
            { "NO RIVAL CARRIER", "ЧУЖОЙ НЕСУЩЕЙ НЕТ" },
            { "> PLUG A FREE CHANNEL INTO THE TUNER, THEN LOCK", "> ВКЛЮЧИТЕ СВОБОДНЫЙ КАНАЛ В ТЮНЕР И ЗАХВАТИТЕ" },
            { "JAMMED", "ЗАГЛУШЁН" },
            { "CARRIER", "НЕСУЩАЯ" },
            { "FREE", "СВОБОДЕН" },
            { "CH{0} {1} GHz {2} {3}", "КАН{0} {1} ГГц {2} {3}" },
            { "CH{0}", "КАН{0}" },
            { "EMPTY", "ПУСТО" },
            { "CH{0} JAMMED", "КАН{0} ЗАГЛУШЁН" },
            { "CH{0} FREE", "КАН{0} СВОБОДЕН" },
            { "{0} TUNER  {1}", "{0} ТЮНЕР  {1}" },
            { "[ LOCK CARRIER ]", "[ ЗАХВАТИТЬ НЕСУЩУЮ ]" },
            { "> CLICK A CHANNEL, THEN THE TUNER", "> КЛИК ПО КАНАЛУ, ПОТОМ ПО ТЮНЕРУ" },

            // SAT-LINK
            { "The robot's signal goes through ONE satellite. Pick one that is IN RANGE (over the station, bright arc).\nIn the robot: keys 1 / 2 / 3. A rival intercept is beaten in the robot feed: [H] tuner, pick a free channel.",
              "Сигнал робота идёт через ОДИН спутник. Выбирайте тот, что В ЗОНЕ (над станцией, яркая дуга).\nВ камере робота: клавиши 1 / 2 / 3. Перехват конкурента отбивается в камере робота: [H] тюнер, свободный канал." },
            { "STATION", "СТАНЦИЯ" },
            { "LEAVING RANGE", "УХОДИТ ИЗ ЗОНЫ" },
            { "IN RANGE", "В ЗОНЕ" },
            { "OUT OF RANGE", "ВНЕ ЗОНЫ" },
            { "back in {0}", "вернётся через {0}" },
            { "[ACTIVE]", "[АКТИВЕН]" },
            { "[USE]", "[ВЫБРАТЬ]" },
            { "BASE RELAY", "РЕТРАНСЛЯТОР БАЗЫ" },
            { "ROBOT SIGNAL: OK via {0}   Q {1}%   LOSS {2}%", "СИГНАЛ РОБОТА: ОК через {0}   К {1}%   ПОТЕРИ {2}%" },
            { "ROBOT SIGNAL: NONE - {0}", "СИГНАЛ РОБОТА: НЕТ - {0}" },
            { "ROBOT SIGNAL: --", "СИГНАЛ РОБОТА: --" },
            { "WARNING: rival {0} is jamming CH{1} via {2}  {3}%\nIn the robot feed: [H] tuner, plug a free channel, LOCK.",
              "ВНИМАНИЕ: конкурент {0} глушит КАН{1} через {2}  {3}%\nВ камере робота: [H] тюнер, свободный канал, ЗАХВАТ." },
            { "HIJACKED: {0} took over {1}.\nNo picture until the carrier is re-locked ([H] in the robot feed).",
              "ПЕРЕХВАТ: {0} захватил {1}.\nКартинки не будет, пока несущую не захватят снова ([H] в камере робота)." },
            { "RETUNING... {0}s", "ПЕРЕСТРОЙКА... {0}с" },
            { "THREATS: none. {0} (rival) on outer orbit.", "УГРОЗ НЕТ. {0} (конкурент) на внешней орбите." },

            // SHOP
            { "PARTS DEPOT // ROBOT UPGRADES", "СКЛАД ДЕТАЛЕЙ // УЛУЧШЕНИЯ РОБОТА" },
            { "BALANCE {0} CR", "БАЛАНС {0} CR" },
            { "{0} ALREADY AT MAX", "{0}: УЖЕ МАКСИМУМ" },
            { "NOT ENOUGH CREDITS - NEED {0} CR", "НЕ ХВАТАЕТ КРЕДИТОВ - НУЖНО {0} CR" },
            { "INSTALLED: {0} MK{1}  -{2} CR", "УСТАНОВЛЕНО: {0} MK{1}  -{2} CR" },
            { "LVL {0}  {1}/{2}", "УР {0}  {1}/{2}" },
            { "NOW   {0}", "СЕЙЧАС {0}" },
            { "NEXT  {0}", "ДАЛЕЕ  {0}" },
            { "[INSTALLED]", "[УСТАНОВЛЕНО]" },
            { "[BUY  {0} CR]", "[КУПИТЬ  {0} CR]" },
            { "BATTERY CELLS", "АККУМУЛЯТОРЫ" },
            { "POWER REGULATOR", "РЕГУЛЯТОР ПИТАНИЯ" },
            { "DRIVE WHEELS", "ВЕДУЩИЕ КОЛЁСА" },
            { "FOG LAMPS", "ПРОТИВОТУМАННЫЕ ФАРЫ" },
            { "MANIPULATOR ARM", "РУКА-МАНИПУЛЯТОР" },
            { "CARGO BAY", "ГРУЗОВОЙ ОТСЕК" },
            { "Bigger battery, longer runs.", "Больше ёмкость — дольше вылазки." },
            { "Battery drains slower.", "Батарея разряжается медленнее." },
            { "Higher top speed and acceleration.", "Выше скорость и разгон." },
            { "High beams that cut through moon dust.", "Дальний свет пробивает лунную пыль." },
            { "Lets the robot pull levers.", "Робот может дёргать рычаги." },
            { "More room for salvage.", "Больше места для добычи." },
            { "{0} CELL", "{0} ЁМК" },
            { "STOCK DRAIN", "ШТАТНЫЙ РАСХОД" },
            { "-{0}% DRAIN", "-{0}% РАСХОД" },
            { "STOCK SPEED", "ШТАТНАЯ СКОРОСТЬ" },
            { "+{0}% SPEED", "+{0}% СКОРОСТЬ" },
            { "STOCK LAMPS", "ШТАТНЫЕ ФАРЫ" },
            { "FOG -{0}%", "ТУМАН -{0}%" },
            { "NO ARM", "НЕТ РУКИ" },
            { "PULLS LEVERS", "ДЁРГАЕТ РЫЧАГИ" },
            { "{0} SLOTS", "{0} СЛОТОВ" },
            { "WHEEL", "КОЛЕСО" },
            { "BATTERY", "АККУМУЛЯТОР" },
            { "HEADLIGHTS", "ФАРЫ" },
            { "BODY PANEL", "ПАНЕЛЬ КОРПУСА" },
            { "WINDOW", "СТЕКЛО" },
            { "CARGO FULL - UNLOAD AT THE CHARGE STATION", "КУЗОВ ПОЛОН - РАЗГРУЗИТЕСЬ НА ЗАРЯДНОЙ СТАНЦИИ" },
            { "{0} LOADED  CARGO {1}/{2}", "{0} В КУЗОВЕ  ГРУЗ {1}/{2}" },
            { "UNLOADED {0} PARTS TO THE REPAIR BAY  +{1} CR", "ВЫГРУЖЕНО ДЕТАЛЕЙ: {0} - В РЕМОНТНЫЙ ОТСЕК  +{1} CR" },
            { "[E] PICK UP {0}", "[E] ПОДОБРАТЬ: {0}" },
            { "SALVAGE NO SIGNAL", "ДЕТАЛИ НЕ НАЙДЕНЫ" },
            { "SALVAGE {0}M  {1}", "ДЕТАЛЬ {0}М  {1}" },
            { "WARN WEATHER FRONT - {0} IN {1}s", "ВНИМ ПОГОДНЫЙ ФРОНТ - {0} ЧЕРЕЗ {1}с" },
            { "E — Take {0}", "E — Взять: {0}" },
            { "Hold mouse wheel — toolbox", "Зажми колёсико — чемоданчик" },
            { "[E] PRESS BUTTON - {0}", "[E] НАЖАТЬ КНОПКУ - {0}" },
            { "BUTTON - MANIPULATOR ARM REQUIRED (SHOP)", "КНОПКА - НУЖНА РУКА-МАНИПУЛЯТОР (МАГАЗИН)" },
            { "BUTTON - DRIVE CLOSER, ARM CAN'T REACH", "КНОПКА - ПОДЪЕДЬ БЛИЖЕ, РУКА НЕ ДОСТАЁТ" },
            { "ARM CAN'T REACH - DRIVE CLOSER TO THE BUTTON", "РУКА НЕ ДОСТАЁТ - ПОДЪЕДЬ К КНОПКЕ" },
            { "GATE", "ВОРОТА" },
            { "PLATFORM", "ПЛАТФОРМА" },
            { "LOCK RELEASED - GATE OPENING", "ЗАМОК СНЯТ - ВОРОТА ОТКРЫВАЮТСЯ" },
            { "GATE OPENING", "ВОРОТА ОТКРЫВАЮТСЯ" },
            { "GATE CLOSING", "ВОРОТА ЗАКРЫВАЮТСЯ" },
            { "PLATFORM GOING UP", "ПЛАТФОРМА ПОДНИМАЕТСЯ" },
            { "PLATFORM GOING DOWN", "ПЛАТФОРМА ОПУСКАЕТСЯ" },
            { "E — Take wrench", "E — Взять гаечный ключ" },
            { "WRENCH", "ГАЕЧНЫЙ КЛЮЧ" },
            { "SCREWDRIVER", "ОТВЁРТКА" },
            { "HAMMER", "МОЛОТОК" },
            { "Hands full", "Руки заняты" },
            { "TOOL BELT", "ПОЯС С ИНСТРУМЕНТАМИ" },
            { "bolt", "болт" },
            { "screw", "винт" },
            { "Aim at a {0} on a fitted part", "Наведитесь на {0} поставленной детали" },
            { "That's a bolt — take the wrench", "Это болт — нужен гаечный ключ" },
            { "That's a screw — take the screwdriver", "Это винт — нужна отвёртка" },
            { "The part sticks out — knock it in with the hammer first", "Деталь торчит — сначала забейте её молотком" },
            { "Screw tight", "Винт закручен" },
            { "Scroll up — tighten {0}  {1}/{2}", "Колесо вверх — закрутить {0}  {1}/{2}" },
            { "LMB — knock the part in  {0}/{1}", "ЛКМ — забить деталь  {0}/{1}" },
            { "Aim at a part that sticks out", "Наведитесь на торчащую деталь" },
            { "Knock in with the hammer: {0} parts", "Забить молотком: деталей {0}" },
            { "Bolts to tighten with the wrench: {0}", "Болтов закрутить ключом: {0}" },
            { "Screws to drive with the screwdriver: {0}", "Винтов закрутить отвёрткой: {0}" },
            { "ON BELT", "НА ПОЯСЕ" },
            { "IN HAND", "В РУКЕ" },
            { "Hold mouse wheel — tool belt", "Зажать колёсико — пояс с инструментами" },
            { "(in hand)", "(в руке)" },
            { "(empty)", "(пусто)" },
            { "Release wheel — put back on the belt", "Отпустить колёсико — повесить на пояс" },
            { "Release wheel — take", "Отпустить колёсико — взять" },
            { "Release wheel — close", "Отпустить колёсико — закрыть" },
            { "Move the mouse to choose", "Мышь влево-вправо — выбор" },
            { "E — Put {0} back on the belt", "E — Повесить на пояс: {0}" },
            { "{0} holster - empty", "{0}: место на поясе пустое" },
            { "{0} — Drop", "{0} — Бросить" },
            { "LMB — fit {0}", "ЛКМ — поставить: {0}" },
            { "Aim at the empty {0} slot on the robot", "Наведитесь на пустое место под «{0}» на роботе" },
            { "Wait until the robot has landed", "Дождитесь, пока робот приземлится" },
            { "No robot needs this part right now", "Сейчас эта деталь роботу не нужна" },
            { "Aim at a bolt on a fitted part", "Наведитесь на болт поставленной детали" },
            { "Bolt tight", "Болт затянут" },
            { "Scroll up — tighten bolt  {0}/{1}", "Колесо вверх — затянуть болт  {0}/{1}" },
            { "Bolts to tighten with the wrench: {0} parts", "Затянуть болты ключом: деталей {0}" },
        };
    }
}
