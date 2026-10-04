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
| [CONTRIBUTING.md](CONTRIBUTING.md) | Как вести работу: ветки, коммиты, PR |
| [.claude/agents/](.claude/agents/) | Роли ИИ-агентов: архитектор, разработчик, тестировщик |

## Статус

🟡 **Этап 2 — скелет приложения.** Макеты утверждены, решение собрано, CI работает.
Прогресс — в [ROADMAP](docs/ROADMAP.md).

## Быстрый старт

Нужен .NET SDK 10.0.100 или новее (`dotnet --version`).

```bash
git clone https://github.com/Kandaulov/labScaner.git
cd labScaner
dotnet build
dotnet test
dotnet run --project src/LabScaner.Web    # http://localhost:5183
```

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
