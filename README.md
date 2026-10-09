# labScaner

Система приёма и первичной проверки студенческих работ для преподавателей вуза.

Студент присылает лабораторную или курсовую на почту с темой
`ИСТ-41 – КорпИС – Иванов Иван Иванович – Лаб №3` → система забирает письмо по IMAP,
кладёт файл в папку преподавателя на Яндекс Диске, проверяет работу через LLM на соответствие
заданию и заносит результат в журнал группы → преподаватель подтверждает оценку и замечания →
студенту уходит письмо с итогом.

> **ИИ — помощник, а не экзаменатор.** Ничего не уходит студенту без подтверждения преподавателя.

## Возможности

- Автоматический приём почты по расписанию и кнопка «Проверить почту сейчас»
- Разбор темы письма, несколько лаб в одном письме, ручное сопоставление нераспознанных писем и файлов
- Раскладка файлов в существующие папки на Яндекс Диске (`{Группа} - ЛР`, `{Группа} - Кр`), версии при пересдаче
- Задания из DOCX: один файл на все лабы (делится по заголовкам) + задание на курсовую
- Проверка ИИ по чек-листу задания с обезличиванием персональных данных
- Три журнала: событий («что ждёт от меня»), хода проверки работы, журнал группы с комментариями
- Дедлайны (зачётная неделя, сессия) и расчёт претендентов на автомат
- Уведомления студентам после подтверждения преподавателем
- Несколько преподавателей, у каждого свои предметы, почта и Диск

## Стек

| Слой | Технология |
|---|---|
| Backend | .NET 10, ASP.NET Core, EF Core + Npgsql |
| UI | Razor Pages + htmx |
| БД | PostgreSQL 17 |
| Фоновые задачи | Hangfire (PostgreSQL) |
| Почта | MailKit (IMAP/SMTP) |
| Файлы | Яндекс Диск REST API |
| Документы | DocumentFormat.OpenXml, PdfPig, Tesseract (OCR сканов) |
| LLM | Внешний провайдер через `ILlmClient` (OpenAI-совместимый API или Anthropic), целевая модель — Claude |
| Тесты | xUnit, Testcontainers, Playwright |
| Развёртывание | Docker Compose, Caddy, GitHub Actions, VPS Timeweb Cloud |

## Документация

| Документ | Содержание |
|---|---|
| [CLAUDE.md](CLAUDE.md) | Контекст проекта для ИИ-ассистента — начинать отсюда |
| [docs/PROJECT.md](docs/PROJECT.md) | Требования, пользователи, сценарии, глоссарий |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Компоненты, модель данных, конвейер обработки |
| [docs/DECISIONS.md](docs/DECISIONS.md) | Архитектурные решения (ADR) и открытые вопросы |
| [docs/ROADMAP.md](docs/ROADMAP.md) | План работ по этапам |
| [docs/design/CLAUDE_DESIGN_PROMPT.md](docs/design/CLAUDE_DESIGN_PROMPT.md) | Промт для макетов интерфейса |
| [docs/design/DESIGN_REVIEW.md](docs/design/DESIGN_REVIEW.md) | Ревью макетов |
| [docs/passport/](docs/passport/) | Технический паспорт системы (DOCX, PDF, исходник) |
| [docs/DEPLOY.md](docs/DEPLOY.md) | Развёртывание на сервере — пошагово, для первого запуска |
| [CONTRIBUTING.md](CONTRIBUTING.md) | Как вести работу: ветки, коммиты, PR |
| [.claude/agents/](.claude/agents/) | Роли ИИ-агентов: архитектор, разработчик, тестировщик |

## Статус

🟡 **Этап 2 — скелет приложения.** Макеты утверждены, решение собрано, CI работает.
Прогресс — в [ROADMAP](docs/ROADMAP.md).

## Быстрый старт

Система целиком (приложение, БД, HTTPS) — одной командой, см. [deploy/README.md](deploy/README.md):

```bash
cd deploy && cp .env.example .env   # заполнить пароли
docker compose up -d
```

Для разработки:


Нужен .NET SDK 10.0.100 или новее (`dotnet --version`).

```bash
git clone https://github.com/Kandaulov/labScaner.git
cd labScaner
dotnet build
dotnet test                               # интеграционным тестам нужен Docker (Testcontainers)

# локальная БД и строка подключения
docker run -d --name labscaner-db -e POSTGRES_USER=labscaner -e POSTGRES_PASSWORD=<пароль> -p 5432:5432 postgres:17-alpine
dotnet user-secrets --project src/LabScaner.Web set ConnectionStrings:Default "Host=localhost;Database=labscaner;Username=labscaner;Password=<пароль>"

# первый администратор (создаётся при старте, если администратора ещё нет)
dotnet user-secrets --project src/LabScaner.Web set Bootstrap:AdminLogin admin
dotnet user-secrets --project src/LabScaner.Web set Bootstrap:AdminPassword "<пароль от 10 символов>"
dotnet user-secrets --project src/LabScaner.Web set Bootstrap:AdminDisplayName "Кандаулов В.М."

dotnet run --project src/LabScaner.Web    # http://localhost:5183, миграции применятся при старте
```

Настройки безопасности:

| Ключ | По умолчанию | Назначение |
|---|---|---|
| `Bootstrap:AdminLogin`, `Bootstrap:AdminPassword`, `Bootstrap:AdminDisplayName` | — | Первый администратор |
| `DataProtection:KeysPath` | `dataprotection-keys` рядом с приложением | Ключи шифрования cookie и токенов; в Docker — том |
| `Security:LoginRequestsPerMinute` | 20 | Запросов к странице входа в минуту с одного адреса |
| `Database:MigrateOnStartup` | `true` | Применять миграции при старте |

Пароль — от 10 символов; после 5 неудачных попыток вход блокируется на 15 минут.

Проверка живости: `GET /health` → `Healthy`.

Структура решения:

| Проект | Назначение |
|---|---|
| `src/LabScaner.Web` | Razor Pages, htmx-эндпоинты, вход, Hangfire Dashboard |
| `src/LabScaner.Core` | Домен: сущности, статусы, правила, порты (`IClock`, `IMailInbox`, …) |
| `src/LabScaner.Infrastructure` | EF Core, MailKit, Яндекс Диск, LLM, извлечение текста |
| `src/LabScaner.Jobs` | Задачи конвейера обработки писем |
| `tests/LabScaner.Core.Tests` | Юнит-тесты домена |
| `tests/LabScaner.Integration.Tests` | Интеграционные тесты: приложение целиком, позже PostgreSQL в Testcontainers |

Версии NuGet-пакетов — только в `Directory.Packages.props`; общие настройки сборки (предупреждения = ошибки,
nullable, анализаторы) — в `Directory.Build.props`.
