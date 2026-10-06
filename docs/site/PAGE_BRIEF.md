# AION2 DPS Meter — страница загрузки (задание для Claude Code)

В этой папке всё для страницы, с которой игроки скачивают AION2 DPS Meter:

- этот файл — что сделать, ссылки, тексты на английском и русском, метатеги;
- `page-preview.html` — образец страницы (откройте в браузере): структура, порядок блоков и оформление;
- `images/` — скриншоты (английский и русский варианты), иконка, картинка для соцсетей;
- `video/` — оверлей во время боя: MP4 для страницы, GIF для мест, где видео нельзя, кадр-заставка.

Все ники на скриншотах и в видео выдуманные, бой постановочный — материалы можно публиковать как есть.

## 1. Что сделать

1. Добавить на сайт страницу программы. Предлагаемый адрес — `/aion2-dps-meter` (можно другой, в стиле сайта).
2. Основной язык — английский, второй — русский. Если на сайте есть переключатель языков, использовать его; если нет —
   переключатель EN / RU на самой странице, как в `page-preview.html`. Русские скриншоты и видео — для русской версии.
3. Скопировать `images/` и `video/` в статические файлы сайта. Не ссылаться на картинки на GitHub.
4. Сделать вёрстку в стиле сайта, но сохранить порядок блоков (раздел 4) и тексты (разделы 5 и 6).
   Оформление из `page-preview.html` — образец: тёмный фон, золотые акценты, как у самой программы.
5. Добавить метатеги (раздел 7).
6. Сделать скачивание прямо с сайта: адрес `/download/aion2-dps-meter` с переадресацией на установщик (раздел 2).
7. Проверить: ширина телефона 375 px, видео играет само без звука и по кругу, у всех картинок есть alt, все ссылки
   открываются, кнопка «Скачать» сразу скачивает `AION2DpsMeter-Setup.exe`.

## 2. Ссылки

| Что | Адрес |
|---|---|
| Скачать установщик (последняя версия, с 0.2.0) | https://github.com/cyberbadger6969/aion2-dps-meter/releases/latest/download/AION2DpsMeter-Setup.exe |
| Страница последнего релиза | https://github.com/cyberbadger6969/aion2-dps-meter/releases/latest |
| Исходный код | https://github.com/cyberbadger6969/aion2-dps-meter |
| Сообщить об ошибке | https://github.com/cyberbadger6969/aion2-dps-meter/issues |
| Лицензия GPL-3.0 | https://github.com/cyberbadger6969/aion2-dps-meter/blob/main/LICENSE |
| Npcap (нужен для работы) | https://npcap.com/#download |

### Скачивание прямо с сайта

Кнопка «Скачать» должна сразу отдавать файл `AION2DpsMeter-Setup.exe` — без перехода на GitHub. Начиная с версии 0.2.0
в каждом релизе есть копии с постоянными именами, поэтому эти адреса всегда отдают самую новую версию:

- установщик: `https://github.com/cyberbadger6969/aion2-dps-meter/releases/latest/download/AION2DpsMeter-Setup.exe`
- без установки: `https://github.com/cyberbadger6969/aion2-dps-meter/releases/latest/download/AION2DpsMeter-win-x64.zip`

**Рекомендуемый способ — свой адрес с переадресацией.** Сделать на сайте адреса
`/download/aion2-dps-meter` и `/download/aion2-dps-meter-zip`, которые отвечают переадресацией (302) на адреса выше.
Посетитель видит ссылку сайта, нажимает — и браузер сразу скачивает файл (сам файл отдаёт быстрый CDN GitHub).
Новые версии на сайте ничего менять не требуют. Примеры для разных движков:

```js
// Next.js — next.config.js
module.exports = {
  async redirects() {
    return [
      { source: '/download/aion2-dps-meter', destination: 'https://github.com/cyberbadger6969/aion2-dps-meter/releases/latest/download/AION2DpsMeter-Setup.exe', permanent: false },
      { source: '/download/aion2-dps-meter-zip', destination: 'https://github.com/cyberbadger6969/aion2-dps-meter/releases/latest/download/AION2DpsMeter-win-x64.zip', permanent: false },
    ];
  },
};
```

```nginx
# nginx
location = /download/aion2-dps-meter     { return 302 https://github.com/cyberbadger6969/aion2-dps-meter/releases/latest/download/AION2DpsMeter-Setup.exe; }
location = /download/aion2-dps-meter-zip { return 302 https://github.com/cyberbadger6969/aion2-dps-meter/releases/latest/download/AION2DpsMeter-win-x64.zip; }
```

```text
# Netlify / Cloudflare Pages — файл _redirects
/download/aion2-dps-meter      https://github.com/cyberbadger6969/aion2-dps-meter/releases/latest/download/AION2DpsMeter-Setup.exe  302
/download/aion2-dps-meter-zip  https://github.com/cyberbadger6969/aion2-dps-meter/releases/latest/download/AION2DpsMeter-win-x64.zip  302
```

```apache
# Apache — .htaccess
Redirect 302 /download/aion2-dps-meter https://github.com/cyberbadger6969/aion2-dps-meter/releases/latest/download/AION2DpsMeter-Setup.exe
Redirect 302 /download/aion2-dps-meter-zip https://github.com/cyberbadger6969/aion2-dps-meter/releases/latest/download/AION2DpsMeter-win-x64.zip
```

Переадресация должна быть временной (302 / `permanent: false`), чтобы браузеры не запоминали её навсегда.

**Если файл должен лежать на самом сайте** (например, сайт не может делать переадресацию): положить
`AION2DpsMeter-Setup.exe` в статические файлы и отдавать его с заголовками `Content-Type: application/octet-stream` и
`Content-Disposition: attachment; filename="AION2DpsMeter-Setup.exe"`. Минус — при каждой новой версии файл нужно
заменять (взять из нового релиза на GitHub). Встроенное обновление в программе в любом случае берёт новые версии с
GitHub, сайт для него не нужен.

**Что увидит игрок при скачивании.** Программа пока не подписана сертификатом, поэтому браузер может спросить
«Файл скачивают редко — сохранить?», а Windows при запуске покажет SmartScreen («Подробнее» → «Выполнить в любом
случае»). Это не зависит от того, с какого сайта скачан файл; на странице об этом сказано в шаге установки.

**Номер версии на странице** можно показывать автоматически — из GitHub API, без ключа:
`GET https://api.github.com/repos/cyberbadger6969/aion2-dps-meter/releases/latest` → `tag_name` (например `v0.1.0`),
`published_at`, `assets[].name` и `assets[].size`. Лимит — 60 запросов в час с одного адреса, поэтому запрашивать с
сервера сайта и кэшировать (хотя бы на час), а не из браузера каждого посетителя. Если API недоступен — показывать
кнопку без номера версии.

## 3. Файлы

Скриншоты сняты в масштабе 1,5×, чтобы были чёткими на экранах высокой плотности. Показывать их в 2/3 от размера в
пикселях (колонка «Показывать»).

| Файл | Размер, px | Показывать | Где на странице | alt (EN) | alt (RU) |
|---|---|---|---|---|---|
| `video/overlay-fight-en.mp4`, `-ru.mp4` | 750×1140, 14,6 с | 375×570 и больше | Первый экран | — (видео, есть подпись) | — |
| `video/overlay-fight-en-poster.png`, `-ru-poster.png` | 750×1140 | — | Заставка видео (`poster`) | — | — |
| `video/overlay-fight-en.gif`, `-ru.gif` | 420×639 | — | Не для страницы: для Discord, форумов, README | — | — |
| `images/overlay-en.png`, `-ru.png` | 750×1140 | 500×760 | Запасной вариант первого экрана (без видео) | Live overlay: party DPS, damage share and boss HP during a boss fight | Оверлей во время боя с боссом: DPS группы, доля урона и HP босса |
| `images/breakdown-dps-en.png`, `-ru.png` | 1350×1020 | 900×680 | «Every skill, every hit» | Fight breakdown: DPS timeline and per-skill damage table | Разбор боя: график DPS и таблица урона по умениям |
| `images/breakdown-accuracy-en.png`, `-ru.png` | 1350×1020 | 900×680 | «Accuracy and rotation» | Accuracy tab: crit, back, front, perfect and smite rates per skill | Вкладка «Точность»: криты, удары в спину и в лоб, идеальные удары по умениям |
| `images/breakdown-rotation-en.png`, `-ru.png` | 1350×1020 | 900×680 | «Accuracy and rotation» | Rotation tab: every cast in order, crits ringed in gold | Вкладка «Ротация»: все применения умений по порядку, криты в золотой рамке |
| `images/boss-timers-en.png`, `-ru.png` | 1170×960 | 780×640 | «Field boss timers» | Field boss respawn timers read from the in-game map | Таймеры возрождения полевых боссов, взятые с карты в игре |
| `images/update-window-en.png`, `-ru.png` | 870×840 | 580×560 | «Always up to date» (с 0.2.0) | Update window: what's new and one-click update | Окно обновления: что нового и обновление в один клик |
| `images/update-banner-en.png`, `-ru.png` | 750×150 | 500×100 | «Always up to date» (с 0.2.0) | The overlay announces a new version | Оверлей сообщает о новой версии |
| `images/overlay-en-transparent.png` | 750×1140 | по месту | Если нужен оверлей поверх своего фона (например, игрового скриншота) | Live overlay | Оверлей |
| `images/social-preview.png` | 1200×630 | — | `og:image`, `twitter:image` | — | — |
| `images/icon-256.png` | 256×256 | 48–96 | Логотип программы в шапке блока, favicon раздела | AION2 DPS Meter icon | Значок AION2 DPS Meter |
| `images/installer-art.png` | 430×824 | по месту | Необязательно: декор у блока установки | — | — |

Видео на странице:

```html
<video autoplay muted loop playsinline preload="metadata" width="375" height="570"
       poster="/media/aion2-dps-meter/overlay-fight-en-poster.png"
       aria-label="AION2 DPS Meter overlay during a boss fight">
  <source src="/media/aion2-dps-meter/overlay-fight-en.mp4" type="video/mp4">
</video>
```

Для посетителей с `prefers-reduced-motion: reduce` видео не запускать само (показать заставку).

## 4. Структура страницы

1. **Первый экран.** Название, подзаголовок, кнопки «Скачать для Windows» и «GitHub», строка с версией и требованиями,
   справа — видео оверлея.
2. **Возможности** — 8 карточек (иконка, заголовок, 1–2 предложения).
3. **Скриншоты с пояснениями** — четыре блока, картинка слева или справа поочерёдно: разбор боя; точность и ротация;
   таймеры боссов; обновления.
4. **Безопасность и открытый код** — короткий блок с оговоркой.
5. **Установка** — три шага, вариант без установщика, требования, горячие клавиши.
6. **Вопросы и ответы** — раскрывающиеся пункты.
7. **Подвал** — авторы данных, лицензия, отказ от ответственности, ссылки.

## 5. Тексты — English

### First screen

- **Title:** AION2 DPS Meter
- **Subtitle:** A free live DPS meter for AION 2 Global (EU / NA). Party DPS, boss HP, a full skill breakdown and field
  boss timers — right on top of the game.
- **Primary button:** Download for Windows
- **Under the button:** v0.2.0 · Windows 10/11, 64-bit · free (fill the version from the GitHub API if possible)
- **Secondary button:** GitHub
- **Badges:** Free · Open source (GPL-3.0) · Works with ExitLag
- **Video caption:** A boss fight in the overlay: places change as the damage comes in.

### Features

1. **Live overlay** — Party DPS, damage and share for every player, the boss's HP and the biggest hit, updated five
   times a second. Class colours and emblems, your own row highlighted, your place in the group.
2. **Shows up when you fight** — The overlay appears when a boss is fought nearby or you hit anything, and hides again
   after the fight. Lock it in place, make it click-through, set the opacity.
3. **Full breakdown** — Click a player: DPS timeline, every skill with hits, average, max and crit rate, accuracy and
   the whole rotation, cast by cast.
4. **Fight history** — Boss fights are saved automatically. Scroll back through earlier fights right in the overlay or
   open the history window.
5. **Field boss timers** — Respawn timers come straight from the game: open the field boss list on the map once and the
   meter keeps counting, separately for each server, with a tray alert before a boss you watch comes back.
6. **Share in chat** — Right-click a player to copy a one-line result for the game chat.
7. **English and Russian** — The interface and skill names in English or Russian; switch at any time.
8. **Updates itself** — A new version is announced in the meter and installed in one click. *(from version 0.2.0)*

### Screenshot blocks

- **Every skill, every hit** — Click any player in the overlay to see their fight: a DPS timeline against the rest of
  the party and a table of every skill — hits, damage, DPS, average and biggest hit, crit rate and share.
- **Accuracy and rotation** — How often each skill crits, lands from behind or in front, or hits perfectly — and every
  cast in order, with critical hits ringed in gold, so you can see exactly where the damage came from.
- **Field boss timers from the game** — Open the field boss list on the in-game map (Map → Exploration → Field
  monsters) and the meter takes the timers from it: who is up, who comes back when, counted down to the second. Each
  server keeps its own timers; ring the bell on a boss to get a tray alert before it respawns.
- **Always up to date** — The meter checks GitHub for new versions. When one is out, the overlay says so; one click
  downloads the installer, checks it and updates the meter — settings, history and timers stay. *(from version 0.2.0)*

### Safety and open source

**Passive and open source.** The meter only listens to your own game's network traffic through Npcap. It never reads
game memory, injects anything or sends anything to the game, and the whole source code is on GitHub. Third-party
tools may still be against the game's terms of service — use it at your own risk.

### Installation

1. **Install Npcap** — the free capture driver Wireshark uses. Keep the default options: they work with ExitLag and
   other ping boosters. The meter's installer checks for Npcap and opens the download page if it is missing.
2. **Run the installer** — download `AION2DpsMeter-Setup.exe` and run it. No administrator rights needed. If Windows
   says "Windows protected your PC", click *More info* → *Run anyway*: the app is not code-signed yet.
3. **Play** — start the meter before you enter a dungeon (the game sends player names on loading screens). The overlay
   appears as soon as the fight starts.

**No installer?** Download the `.zip`, unzip it anywhere and run `AION2DpsMeter.exe`.

**Requirements:** Windows 10 or 11 (64-bit) · AION 2 on Global servers (EU or NA) · Npcap (free) · about 200 MB of disk space.

**Hotkeys** (change them in Settings):

| Keys | Action |
|---|---|
| Ctrl+Shift+D | Show / hide the overlay |
| Ctrl+Shift+R | Reset the meter |
| Ctrl+Shift+L | Click-through mode |
| Ctrl+Shift+T | Boss timers |

### FAQ

- **Can I get banned for this?** — The meter does not touch the game: no memory reading, no injection, nothing is sent
  to the game. Still, any third-party tool may be against the game's terms of service — use it at your own risk.
- **Some players show as "#12345" instead of a name.** — The game sends names on loading screens. Start the meter
  before entering a dungeon; players who were already around get their names after the next loading screen.
- **Does it work with ExitLag or a VPN?** — Yes with ExitLag and other ping boosters. If a VPN hides the game
  connection, choose the network adapter in Settings → Capture.
- **The boss timers are empty.** — Open the field boss list on the in-game map (Map → Exploration → Field monsters)
  once: the meter reads the timers from it and keeps counting.
- **My antivirus or SmartScreen complains.** — The app is new, not code-signed, and it captures network traffic, which
  some antiviruses dislike. It is open source: you can read or build the code yourself.
- **My crit rate on a crowded field boss is almost zero.** — On busy field bosses the game reports very few critical
  hits for some classes; the meter shows exactly what the server sends. On other targets the numbers look as usual.
- **Korean or Taiwanese servers?** — Not supported: the meter reads the protocol of the Global client (EU / NA).
- **Where are my settings and fights? How do I uninstall?** — Everything lives in `%AppData%\AionMeter`. Uninstall from
  Windows Settings → Apps: it asks whether to delete settings and history as well.

### Footer

AION2 DPS Meter is free and open source under GPL-3.0. Game data tables come from the GPL-3.0 projects
A2Tools-DPS-Meter (taengu) and AIon2-Dps-Meter (Kuroukihime); skill icons from the official AION 2 CDN; boss portraits
from MetaBot.GG. AION 2 and its game data and art are © NCSOFT. This project is not affiliated with NCSOFT.
Third-party tools may be against the game's terms of service — use at your own risk.

## 6. Тексты — Русский

### Первый экран

- **Заголовок:** AION2 DPS Meter
- **Подзаголовок:** Бесплатный DPS-метр для AION 2 Global (EU / NA). Урон группы, HP босса, подробный разбор умений и
  таймеры полевых боссов — прямо поверх игры.
- **Главная кнопка:** Скачать для Windows
- **Под кнопкой:** v0.2.0 · Windows 10/11, 64-бит · бесплатно (номер версии — из GitHub API, если получится)
- **Вторая кнопка:** GitHub
- **Значки:** Бесплатно · Открытый код (GPL-3.0) · Работает с ExitLag
- **Подпись к видео:** Бой с боссом в оверлее: места меняются по ходу боя.

### Возможности

1. **Оверлей в реальном времени** — DPS, урон и доля каждого игрока, HP босса и самый сильный удар, пять обновлений в
   секунду. Цвета и эмблемы классов, ваша строка выделена, ваше место в группе.
2. **Появляется в бою** — Оверлей показывается, когда рядом бьют босса или вы кого-то ударили, и прячется после боя.
   Его можно закрепить, сделать прозрачным для кликов, настроить прозрачность.
3. **Полный разбор** — Нажмите на игрока: график DPS, каждое умение с числом ударов, средним и максимальным уроном и
   процентом критов, точность и вся ротация по порядку.
4. **История боёв** — Бои с боссами сохраняются сами. Листайте прошлые бои прямо в оверлее или в окне истории.
5. **Таймеры полевых боссов** — Таймеры берутся прямо из игры: откройте один раз список полевых боссов на карте, и метр
   продолжит отсчёт — отдельно для каждого сервера, с уведомлением перед возрождением отмеченного босса.
6. **Результат в чат** — Правый клик по игроку копирует результат одной строкой для игрового чата.
7. **Английский и русский** — Интерфейс и названия умений на английском или русском, переключение в любой момент.
8. **Обновляется сам** — Метр сообщает о новой версии и ставит её в один клик. *(с версии 0.2.0)*

### Блоки со скриншотами

- **Каждое умение, каждый удар** — Нажмите на любого игрока в оверлее, чтобы увидеть его бой: график DPS на фоне
  остальной группы и таблицу всех умений — удары, урон, DPS, средний и самый сильный удар, процент критов и доля.
- **Точность и ротация** — Как часто каждое умение критует, попадает в спину или в лоб, наносит идеальный удар — и все
  применения по порядку, криты в золотой рамке: видно, откуда взялся урон.
- **Таймеры полевых боссов из игры** — Откройте на карте в игре список полевых боссов (Карта → Исследование → Полевые
  монстры), и метр возьмёт таймеры оттуда: кто жив, кто когда вернётся, с отсчётом до секунды. У каждого сервера свои
  таймеры; включите колокольчик у босса, чтобы получить уведомление перед его возрождением.
- **Всегда свежая версия** — Метр проверяет новые версии на GitHub. Когда выходит новая, оверлей об этом сообщает; один
  клик — и установщик скачан, проверен, метр обновлён. Настройки, история и таймеры сохраняются. *(с версии 0.2.0)*

### Безопасность и открытый код

**Только слушает и открыт.** Метр слушает лишь сетевой трафик вашей игры через Npcap. Он не читает память игры, ничего в
неё не внедряет и ничего не отправляет, а весь исходный код лежит на GitHub. Тем не менее сторонние программы могут
нарушать правила игры — используйте на свой риск.

### Установка

1. **Установите Npcap** — бесплатный драйвер захвата трафика (его же использует Wireshark). Настройки оставьте по
   умолчанию: с ними работают ExitLag и другие ускорители пинга. Установщик метра сам проверит Npcap и откроет
   страницу загрузки, если его нет.
2. **Запустите установщик** — скачайте `AION2DpsMeter-Setup.exe` и запустите. Права администратора не нужны. Если
   Windows покажет «Система Windows защитила ваш компьютер», нажмите *Подробнее* → *Выполнить в любом случае*:
   программа пока не подписана сертификатом.
3. **Играйте** — запускайте метр до входа в подземелье (ники игроков игра присылает на экранах загрузки). Оверлей
   появится, как только начнётся бой.

**Без установщика?** Скачайте `.zip`, распакуйте в любую папку и запустите `AION2DpsMeter.exe`.

**Требования:** Windows 10 или 11 (64-бит) · AION 2 на серверах Global (EU или NA) · Npcap (бесплатно) · около 200 МБ на диске.

**Горячие клавиши** (меняются в настройках):

| Клавиши | Действие |
|---|---|
| Ctrl+Shift+D | Показать / скрыть оверлей |
| Ctrl+Shift+R | Сбросить метр |
| Ctrl+Shift+L | Клики сквозь оверлей |
| Ctrl+Shift+T | Таймеры боссов |

### Вопросы и ответы

- **Можно ли за это получить бан?** — Метр не трогает игру: не читает память, ничего не внедряет и ничего не отправляет
  в игру. Тем не менее любые сторонние программы могут нарушать правила игры — используйте на свой риск.
- **Некоторые игроки показаны как «#12345» вместо ника.** — Ники игра присылает на экранах загрузки. Запускайте метр
  до входа в подземелье; у тех, кто уже был рядом, ники появятся после следующей загрузки.
- **Работает ли с ExitLag или VPN?** — С ExitLag и другими ускорителями пинга — да. Если VPN прячет соединение игры,
  выберите сетевой адаптер в Настройках → Захват.
- **Таймеры боссов пустые.** — Откройте один раз список полевых боссов на карте в игре (Карта → Исследование → Полевые
  монстры): метр возьмёт таймеры оттуда и продолжит отсчёт.
- **Ругается антивирус или SmartScreen.** — Программа новая, не подписана сертификатом и перехватывает сетевой трафик,
  что не нравится некоторым антивирусам. Код открыт: его можно прочитать или собрать самому.
- **На полевом боссе почти нет критов.** — На многолюдных полевых боссах игра у некоторых классов почти не сообщает о
  критах; метр показывает ровно то, что присылает сервер. На других целях цифры обычные.
- **Корейские или тайваньские серверы?** — Не поддерживаются: метр читает протокол глобального клиента (EU / NA).
- **Где хранятся настройки и бои? Как удалить?** — Всё лежит в `%AppData%\AionMeter`. Удаление — Параметры Windows →
  Приложения; при удалении метр спросит, стереть ли настройки и историю.

### Подвал

AION2 DPS Meter — бесплатная программа с открытым кодом под лицензией GPL-3.0. Таблицы игровых данных взяты из
проектов A2Tools-DPS-Meter (taengu) и AIon2-Dps-Meter (Kuroukihime) под GPL-3.0; иконки умений — с официального CDN
AION 2; портреты боссов — с MetaBot.GG. AION 2, игровые данные и арт © NCSOFT. Проект не связан с NCSOFT. Сторонние
программы могут нарушать правила игры — используйте на свой риск.

## 7. Метатеги

| | English | Русский |
|---|---|---|
| `<title>` | AION2 DPS Meter — free DPS meter for AION 2 (EU / NA) | AION2 DPS Meter — бесплатный DPS-метр для AION 2 (EU / NA) |
| `description` | Free, open-source live DPS overlay for AION 2 Global: party DPS, boss HP, skill breakdown, accuracy, rotation, fight history and field boss respawn timers. Windows 10/11. | Бесплатный DPS-метр с открытым кодом для AION 2 Global: урон группы, HP босса, разбор умений, точность, ротация, история боёв и таймеры полевых боссов. Windows 10/11. |
| `og:image` / `twitter:image` | `images/social-preview.png` (1200×630) | то же |
| `twitter:card` | `summary_large_image` | то же |

Разметка для поисковиков (необязательно), JSON-LD:

```json
{
  "@context": "https://schema.org",
  "@type": "SoftwareApplication",
  "name": "AION2 DPS Meter",
  "operatingSystem": "Windows 10, Windows 11",
  "applicationCategory": "GameApplication",
  "offers": { "@type": "Offer", "price": "0", "priceCurrency": "USD" },
  "downloadUrl": "https://github.com/cyberbadger6969/aion2-dps-meter/releases/latest",
  "softwareVersion": "0.2.0",
  "license": "https://www.gnu.org/licenses/gpl-3.0.html"
}
```

## 8. Важно

- Это отдельная программа с открытым кодом. Не называть её официальным продуктом NCSOFT.
- Не обещать, что за неё не банят; блок «Безопасность» и строка в подвале должны остаться.
- Версия 0.2.0 уже вышла: пометки «с версии 0.2.0» можно показывать как «новое» или убрать.
- При выходе новых версий страницу менять не нужно: кнопка ведёт на последний релиз. Новые скриншоты собираются
  скриптом `tools/make-site-kit.ps1` в репозитории программы.
