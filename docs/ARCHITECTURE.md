# labScaner — архитектура

## 1. Общая схема

Один процесс ASP.NET Core (веб-интерфейс + фоновые задачи Hangfire) и PostgreSQL.
Модульный монолит: модули разделены по папкам/проектам, общаются через сервисы и БД.

```
             IMAP                         SMTP
  Почтовый ящик ──────┐            ┌──────────► Студент
                      ▼            │
 ┌────────────────────────────────────────────────────────┐
 │  labScaner (ASP.NET Core, .NET 10)                     │
 │                                                        │
 │  Mail.Ingest ─► Parsing ─► Storage ─► AiCheck ─► Journal│
 │     (job)       (тема)    (Я.Диск)    (LLM)     (БД)    │
 │                                          ▲        │     │
 │  Web UI (Razor Pages + htmx) ────────────┘        │     │
 │     преподаватель: журнал, карточка, настройки    ▼     │
 │                                      Notifications(job) │
 └──────────────┬─────────────────────────────┬───────────┘
                │                             │
           PostgreSQL 17              Яндекс Диск REST API
     (данные + очередь Hangfire)       LLM-провайдер (HTTP)
```

## 2. Конвейер обработки письма

Каждый шаг — отдельная задача Hangfire; состояние хранится в БД, поэтому шаги можно повторять.

| # | Шаг | Вход → выход | Статус сдачи после шага |
|---|---|---|---|
| 1 | `PollInboxJob` (cron, 5 мин) | IMAP → `InboxMessage` (сырые заголовки, вложения во временную папку Диска) | — |
| 2 | `ParseMessageJob` | `InboxMessage` → `Submission` или статус «Нераспознано» | `Received` / `Unrecognized` |
| 3 | `StoreFilesJob` | временная папка → постоянная, `SubmissionFile` | `Stored` |
| 4 | `ExtractTextJob` | файл → текст (OpenXml / PdfPig) | `TextReady` / `NeedsManual` |
| 5 | `AiCheckJob` | текст + задание + чек-лист → `AiCheck` | `AiChecked` / `AiFailed` |
| 6 | Преподаватель | `Review` | `Accepted` / `Returned` |
| 7 | `SendNotificationJob` | `Notification` → SMTP | `Notified` |

Идемпотентность: уникальный индекс по `InboxMessage.MessageId`; каждый job проверяет текущий статус
перед выполнением.

## 3. Модель данных (черновик)

```
User(id, login, password_hash, display_name, roles: Teacher|Admin)   -- ASP.NET Core Identity
TeacherConnection(teacher_id, imap_*, smtp_*, yandex_token_encrypted, mail_poll_interval,
                  mail_poll_enabled)                       -- подключения преподавателя

-- Общее для системы: Group, Student, StudentEmail, Term, настройки LLM
-- Принадлежит преподавателю (teacher_id): Subject и всё под ним, InboxMessage, Notification,
--   AttentionItem, EventLog (с teacher_id NULL для системных событий)

Subject(id, teacher_id, name, code,                           -- code: КорпИС | ОС | СПП
        aliases[],                                            -- допустимые написания в теме письма
        final_assessment: Exam|Pass|GradedPass,
        ai_reference_text, disk_path_template, is_active)
Group(id, name, direction, admission_year)                    -- direction: ИСТ
Term(id, academic_year, season: Autumn|Spring,                -- 2026-2027, осенний
     credit_week_start, session_start)                        -- календарь → дедлайны
SubjectTerm(id, subject_id, term_id, study_semester,          -- «КорпИС, 2026-2027, 7 сем»
            disk_root_path)                                   -- существующая папка на Диске
SubjectTermGroup(subject_term_id, group_id)
Student(id, group_id, last_name, first_name, middle_name, is_active)
StudentEmail(id, student_id, email)                          -- у студента может быть несколько адресов

SubjectTerm += labs_task_file_path, coursework_task_file_path  -- два DOCX с заданиями

Assignment(id, subject_term_id, kind: Lab|Coursework, number, title,
           task_text,                                         -- фрагмент общего DOCX по заголовку
           checklist_json, ai_check_enabled,
           deadline_override NULL)                            -- иначе: Lab → credit_week_start,
                                                              --        Coursework → session_start

InboxMessage(id, message_id UNIQUE, from_email, subject_raw, received_at,
             status: New|Parsed|Unrecognized|Ignored|Error, error_text, submission_id NULL)

Submission(id, student_id, assignment_id, version, received_at, inbox_message_id,
           status, is_current, is_on_time)
SubmissionFile(id, submission_id, file_name, content_type, size, sha256,
               disk_path, extracted_text NULL)

AiCheck(id, submission_id, provider, model, prompt_version, started_at, finished_at,
        result_json, suggested_grade NULL, summary, status)
Review(id, submission_id, decision: Accepted|Returned, grade NULL, comment,
       reviewed_at, notify_requested)

FinalAssessment(id, subject_term_id, student_id, is_automat,
                coursework_grade NULL, coursework_date NULL,
                exam_grade NULL, exam_date NULL, pass_mark NULL, pass_date NULL)

Notification(id, student_id, created_at, body_html, status: Pending|Sent|Failed,
             sent_at, attempts, last_error)
NotificationItem(notification_id, review_id)               -- какие работы вошли в письмо

ProcessingStep(id, submission_id, step: Received|Stored|TextExtracted|AiChecked|Reviewed|Notified,
               status: Pending|Running|Done|Failed|Skipped, started_at, finished_at,
               attempts, error_text)                         -- журнал проверки работы
Comment(id, submission_id, author: Ai|Teacher|System, kind: Remark|SentToStudent|Note,
        text, created_at)                                   -- история комментариев по лабе
AttentionItem(id, kind: ReviewPending|Unrecognized|AutomatCandidates|NotificationsReady|
              DeadlineSoon|ServiceError, ref_type, ref_id, title, created_at,
              resolved_at NULL)                             -- «Что ждёт от меня»
EventLog(id, at, level, source, message, ref_type, ref_id)  -- полная лента событий
MailPollRun(id, started_at, finished_at, trigger: Schedule|Manual,
            fetched, recognized, unrecognized, error_text)
Setting(key, value_encrypted)
```

Признак «претендует на автомат» вычисляется по правилу (ADR-013, Q11), не хранится.

Журнал не хранится отдельной таблицей — это представление (SQL view / запрос) над
`Student × Assignment` с последней `Submission`, её `AiCheck` и `Review`, плюс `FinalAssessment`.

## 4. Структура решения (.NET)

```
labScaner/
  src/
    LabScaner.Web/            -- ASP.NET Core: Razor Pages, htmx-эндпоинты, auth, Hangfire dashboard
    LabScaner.Core/           -- домен: сущности, статусы, правила, интерфейсы портов
    LabScaner.Infrastructure/ -- EF Core, миграции, MailKit, Яндекс Диск, LLM, извлечение текста
    LabScaner.Jobs/           -- задачи конвейера (можно слить с Infrastructure, если мало кода)
  tests/
    LabScaner.Core.Tests/         -- юнит: разбор темы, статусы, сборка промта
    LabScaner.Integration.Tests/  -- Testcontainers PostgreSQL, фейки IMAP/Диска/LLM
    LabScaner.E2E.Tests/          -- Playwright по ключевым сценариям
    fixtures/                     -- образцы писем (.eml), PDF, DOCX
  deploy/
    docker-compose.yml, Caddyfile, .env.example
  docs/
  .github/workflows/
```

Порты (интерфейсы в Core):
`IMailInbox`, `IMailSender`, `IFileStorage`, `ITextExtractor`, `ILlmClient`, `IClock`.

## 5. Яндекс Диск

- OAuth-приложение с правами `cloud_api:disk.read` + `cloud_api:disk.write` (ADR-011).
- Временная папка: `disk:/Приложения/LabScaner/_incoming/{InboxMessageId}/`.
- Постоянное место: `{SubjectTerm.disk_root_path}/{Группа} - ЛР/Лаб 03 - Иванов И.И. - v1.docx`,
  курсовые — `{Группа} - Кр/Курсовая - Иванов И.И. - v1.docx` (ADR-011).
  Пример корня: `30 Политех/03 КорпИС/10 КорпИС - Отчетные документы/2026-2027 КорпИС ИСТ - 7 сем`.
- Корень предлагается по `Subject.disk_path_template` с подстановками `{Учебный год}`, `{Направление}`,
  `{N}`; преподаватель подтверждает или правит путь; система проверяет, что папка существует.
- Задания: `{disk_root_path}/Задания/` (или ссылка на уже лежащий на Диске DOCX).
- Имена папок санитизируются (запрещённые символы, длина). Адаптер `IFileStorage` не имеет методов
  удаления/перезаписи — только создание папок, загрузка новых файлов, перемещение из `_incoming`.

## 6. LLM

- `ILlmClient.CheckAsync(CheckRequest) → CheckResult` — вход: задание, чек-лист, справочные материалы,
  текст работы; выход строго по JSON-схеме.
- Перед отправкой — обезличивание: удаляются ФИО студента (из справочника), e-mail, номер группы.
- Длинные работы — усечение / сжатие по разделам; лимит токенов в настройках.
- Версия промта хранится в `AiCheck.prompt_version` — чтобы сравнивать результаты.
- Провайдер — Timeweb Cloud AI Gateway через OpenAI-совместимый API (ADR-014); base URL, ключ, модель — в настройках.

## 7. Безопасность

- ASP.NET Core Identity: несколько пользователей, роли Teacher/Admin, блокировка после попыток,
  rate limiting на `/login`.
- Изоляция данных преподавателей — глобальный query filter EF Core по `teacher_id` (ADR-016).
- Hangfire Dashboard — только для администратора.
- Токены IMAP/SMTP/Диска/LLM — в переменных окружения или зашифрованы в `Setting` (Data Protection API).
- HTTPS — Caddy с автоматическим сертификатом.

## 8. Развёртывание

- `docker-compose.yml`: `app`, `db` (postgres:17), `caddy`; том для БД; ключи Data Protection в томе.
- CI (GitHub Actions): build → test (с Testcontainers) → образ в GHCR.
- CD: по тегу — `ssh` на VPS, `docker compose pull && up -d`, миграции применяются при старте.
- Бэкап: ежедневный `pg_dump` в отдельную папку (опционально — на тот же Яндекс Диск).
