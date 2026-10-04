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
 │  Mail.Ingest ─► Parsing ─► Storage ─► Text/OCR ─► AiCheck ─► Journal │
 │     (job)    (тема+файлы) (Я.Диск)   (Tesseract)   (LLM)     (БД)    │
 │                                                     ▲        │       │
 │  Web UI (Razor Pages + htmx) ───────────────────────┘        │       │
 │     преподаватель: журнал, карточка, настройки               ▼       │
 │                                                 Notifications(job)   │
 └──────────────┬─────────────────────────────┬───────────┘
                │                             │
           PostgreSQL 17              Яндекс Диск REST API
     (данные + очередь Hangfire)       LLM-провайдер (HTTP)
```

## 2. Конвейер обработки письма

Каждый шаг — отдельная задача Hangfire; состояние хранится в БД, поэтому шаги можно повторять.

| # | Шаг | Вход → выход | Статус сдачи после шага |
|---|---|---|---|
| 1 | `PollInboxJob` (cron, 5 мин) | IMAP (новые UID) → фильтр «сдача работы» (ADR-024) → `InboxMessage` + `InboxAttachment` (PDF/DOCX во временную папку Диска); прочие письма пропускаются | — |
| 2 | `ParseMessageJob` | тема → группа, предмет, студент, **набор работ**; каждый файл → работа (ADR-022) → по `Submission` на работу; несопоставленные файлы → «Нераспознанные» | `Received` / письмо `Unrecognized` / `PartiallyParsed` |
| 3 | `StoreFilesJob` | временная папка → постоянная, `SubmissionFile` | `Stored` |
| 4 | `ExtractTextJob` | DOCX → OpenXml; PDF → PdfPig; PDF без текста → растр + Tesseract (ADR-019) | `TextReady` / `NeedsManual` |
| 5 | `AiCheckJob` | текст + задание + чек-лист → `AiCheck` (пропускается, если ИИ выключен) | `AiChecked` / `AiFailed` / `AwaitingReview` |
| 6 | Преподаватель | `Review` | `Accepted` / `Returned` |
| 7 | `SendNotificationJob` | `Notification` → SMTP (только после явной отправки) | `Notified` |

Идемпотентность: уникальный индекс по `InboxMessage.message_id` и по (`inbox_message_id`, `index`)
вложения; каждый job проверяет текущий статус перед выполнением.

Повторы (ADR-021): с backoff повторяются только временные сбои (сеть, таймаут, недоступность IMAP/SMTP,
Диска, LLM). Пустой текст после OCR — не сбой: сразу `NeedsManual`, без повторов.

### Сопоставление файлов с работами (ADR-022)

1. Номера работ из темы: `Лаб №3, 4`, `Лаб 3-5`, `ЛР 3,4` → {3, 4, 5}.
2. Для каждого файла по порядку: номер в имени файла → заголовок «Лабораторная работа №N» на первой
   странице → исключение (одна работа и один файл остались) → в теме одна работа (все файлы к ней).
3. Номер из файла, которого нет в теме, автоматически не принимается — ручное сопоставление.
4. Распознанные файлы идут дальше сразу; остальные — в «Нераспознанные» с причиной.

### Статусы ячейки журнала (ADR-017)

| Статус ячейки | Из статусов сдачи |
|---|---|
| Не сдано | нет `Submission` |
| В обработке | `Received`, `Stored`, `TextReady` (ИИ ещё работает) |
| Ждёт проверки | `AiChecked`, `AwaitingReview` (ИИ выключен — без метки «ИИ») |
| Нужна ручная проверка | `NeedsManual`, `AiFailed` |
| Принято в срок / после срока | `Accepted`, сравнение `reviewed_at` с дедлайном |
| На доработку | `Returned` (до прихода новой версии) |

Пометки поверх статуса: версия (`version > 1`), «уведомление отправлено», «есть комментарии».

## 3. Модель данных (черновик)

```
User(id, login, password_hash, display_name, roles: Teacher|Admin)   -- ASP.NET Core Identity
TeacherConnection(teacher_id, imap_*, smtp_*, yandex_token_encrypted, mail_poll_interval,
                  mail_poll_enabled,
                  imap_uid_validity, imap_last_uid, mail_since) -- ADR-024: ящик не изменяется

-- Общее для системы: Group, Student, StudentEmail, Term, настройки LLM
-- Принадлежит преподавателю (teacher_id): Subject и всё под ним, InboxMessage, Notification,
--   AttentionItem, EventLog (с teacher_id NULL для системных событий)

Subject(id, teacher_id, name, code,                           -- code: КорпИС | ОС | СПП
        aliases[],                                            -- допустимые написания в теме письма
        final_assessment: Exam|Pass|GradedPass,
        ai_reference_text, disk_path_template, is_active)
Group(id, name, name_key, direction, admission_year)          -- «ИСТ-41»; name_key — без учёта регистра
Term(id, academic_year, season: Autumn|Spring,                -- 2026-2027, осенний
     credit_week_start, session_start)                        -- календарь → дедлайны
SubjectTerm(id, subject_id, term_id, study_semester,          -- «КорпИС, 2026-2027, 7 сем»
            disk_root_path,                                   -- существующая папка на Диске
            labs_task_file_path, coursework_task_file_path)   -- два DOCX с заданиями
SubjectTermGroup(subject_term_id, group_id)
Student(id, group_id, last_name, first_name, middle_name, is_active)
StudentEmail(id, student_id, email, source: Import|Auto|Manual, created_at)  -- адреса копятся по письмам (ADR-024)
CourseworkTopic(subject_term_id, student_id, topic)           -- ADR-025: тема курсовой

Assignment(id, subject_term_id, kind: Lab|Coursework, number, title,
           task_text,                                         -- фрагмент общего DOCX по заголовку
           checklist_json, ai_check_enabled,
           deadline_override NULL)                            -- иначе: Lab → credit_week_start,
                                                              --        Coursework → session_start

InboxMessage(id, message_id UNIQUE, from_email, subject_raw, received_at,
             parsed_group_id NULL, parsed_subject_term_id NULL, parsed_student_id NULL,
             parsed_numbers int[] NULL,                       -- работы из темы (ADR-022)
             status: New|Parsed|PartiallyParsed|Unrecognized|Ignored|Error, error_text)
InboxAttachment(id, inbox_message_id, index, file_name, content_type, size, sha256,
                temp_disk_path, is_accepted,                  -- ADR-023: только PDF и DOCX
                assignment_id NULL, submission_id NULL,       -- результат сопоставления
                match_method: Subject|FileName|Content|Elimination|Manual NULL,
                unmatched_reason NULL,                        -- «номер не найден», «лабы №7 нет в теме»
                UNIQUE(inbox_message_id, index))

Submission(id, student_id, assignment_id, version, received_at, inbox_message_id,  -- много сдач на одно письмо
           status: Received|Stored|TextReady|NeedsManual|AiChecked|AiFailed|AwaitingReview|
                   Accepted|Returned,
           is_current, is_on_time)
SubmissionFile(id, submission_id, inbox_attachment_id, file_name, content_type, size, sha256,
               disk_path, extracted_text NULL,
               text_source: Docx|PdfText|Ocr|None)            -- ADR-019

AiCheck(id, submission_id, provider, model, prompt_version, started_at, finished_at,
        result_json, suggested_grade NULL, summary, status)
Review(id, submission_id, decision: Accepted|Returned, grade NULL, comment,
       reviewed_at, notify_requested,
       source: Card|QuickMark)                                -- быстрая отметка в ячейке журнала

FinalAssessment(id, subject_term_id, student_id, is_automat,
                coursework_review_id NULL → Review,           -- ADR-020: оценка курсовой — в решении
                exam_grade NULL, exam_date NULL, pass_mark NULL, pass_date NULL)

Notification(id, student_id, subject_term_id,                 -- ADR-018: письмо = студент × предмет в семестре
             created_at, body_html, status: Draft|Queued|Sent|Failed,
             sent_at, attempts, last_error)
NotificationItem(notification_id, review_id)               -- какие решения вошли в письмо
-- Пока письмо в статусе Draft, новые решения по этому студенту и предмету добавляются в него.

ProcessingStep(id, submission_id,
               step: Received|Parsed|Stored|TextExtracted|AiChecked|Reviewed|Notified,
               status: Pending|Running|Done|Failed|Skipped, started_at, finished_at,
               attempts, error_text)                         -- журнал проверки работы
Comment(id, submission_id, author: Ai|Teacher|System, kind: Remark|SentToStudent|Note,
        text, created_at)                                   -- история комментариев по лабе
AttentionItem(id, kind: ReviewPending|NeedsManual|Unrecognized|AutomatCandidates|NotificationsReady|
              DeadlineSoon|ServiceError|TokenExpiring, ref_type, ref_id, title, created_at,
              resolved_at NULL)                             -- «Что ждёт от меня»
EventLog(id, at, level, source, message, ref_type, ref_id)  -- полная лента событий
MailPollRun(id, started_at, finished_at, trigger: Schedule|Manual,
            fetched, skipped, recognized, unrecognized, error_text)  -- skipped: не сдача работы (ADR-024)
Setting(key, value_encrypted)
```

Признак «претендует на автомат» вычисляется по правилу (ADR-013), не хранится.

Журнал не хранится отдельной таблицей — это представление (SQL view / запрос) над
`Student × Assignment` с последней `Submission`, её `AiCheck` и `Review`, плюс `FinalAssessment`
(курсовая — через `coursework_review_id`).

## 4. Структура решения (.NET)

```
labScaner/
  src/
    LabScaner.Web/            -- ASP.NET Core: Razor Pages, htmx-эндпоинты, auth, Hangfire dashboard
    LabScaner.Core/           -- домен: сущности, статусы, правила, разбор темы, сопоставление файлов
    LabScaner.Infrastructure/ -- EF Core, миграции, MailKit, Яндекс Диск, LLM, извлечение текста, OCR
    LabScaner.Jobs/           -- задачи конвейера (можно слить с Infrastructure, если мало кода)
  tests/
    LabScaner.Core.Tests/         -- юнит: разбор темы (списки, диапазоны), сопоставление файлов, статусы,
                                  --       сборка промта, сборка письма
    LabScaner.Integration.Tests/  -- Testcontainers PostgreSQL, фейки IMAP/Диска/LLM/OCR
    LabScaner.E2E.Tests/          -- Playwright по ключевым сценариям
    fixtures/                     -- образцы писем (.eml, в т.ч. с несколькими лабами), PDF (сканы, PDF с Mac), DOCX
  deploy/
    docker-compose.yml, Caddyfile, .env.example
  docs/
  .github/workflows/
```

Порты (интерфейсы в Core):
`IMailInbox`, `IMailSender`, `IFileStorage`, `ITextExtractor`, `IOcrEngine`, `ILlmClient`, `IClock`.

## 5. Яндекс Диск

- OAuth-приложение с правами `cloud_api:disk.read` + `cloud_api:disk.write` (ADR-011).
- Временная папка: `disk:/Приложения/LabScaner/_incoming/{InboxMessageId}/`.
- Постоянное место: `{SubjectTerm.disk_root_path}/{Группа} - ЛР/Лаб 03 - Иванов И.И. - v1.docx`,
  курсовые — `{Группа} - Кр/Курсовая - Иванов И.И. - v1.docx` (ADR-011). Если у сдачи несколько файлов —
  `Лаб 03 - Иванов И.И. - v1 (2).pdf` и т.д.
  Пример корня: `30 Политех/03 КорпИС/10 КорпИС - Отчетные документы/2026-2027 КорпИС ИСТ - 7 сем`.
- Корень предлагается по `Subject.disk_path_template` с подстановками `{Учебный год}`, `{Направление}`,
  `{N}`; преподаватель подтверждает или правит путь; система проверяет, что папка существует.
- Задания: `{disk_root_path}/Задания/` (или ссылка на уже лежащий на Диске DOCX).
- Имена папок санитизируются (запрещённые символы, длина). Адаптер `IFileStorage` не имеет методов
  удаления/перезаписи — только создание папок, загрузка новых файлов, перемещение из `_incoming`.
- Несопоставленные файлы остаются в `_incoming` до ручного сопоставления.
- За 7 дней до истечения OAuth-токена — задача `TokenExpiring` в «Что ждёт от меня».

## 6. Извлечение текста и LLM

- DOCX — OpenXml; PDF — PdfPig. Если символов на страницу меньше порога — PDF считается сканом:
  страницы растрируются (PDFium/Docnet) и распознаются Tesseract `rus+eng` локально (ADR-019).
  Изображения страниц наружу не уходят.
- Для сопоставления файла с лабой по содержимому (ADR-022) читаются первые ~1500 символов
  на шаге разбора; полный текст извлекается позже, на шаге 4.
- `ILlmClient.CheckAsync(CheckRequest) → CheckResult` — вход: задание, чек-лист, справочные материалы,
  текст работы; выход строго по JSON-схеме. Для OCR-текста в промте пометка «текст распознан со скана,
  возможны ошибки распознавания — не снижай оценку за опечатки».
- Перед отправкой — обезличивание: удаляются ФИО студента (из справочника), e-mail, номер группы.
- Длинные работы — усечение / сжатие по разделам; лимит токенов в настройках.
- Версия промта хранится в `AiCheck.prompt_version` — чтобы сравнивать результаты.
- Провайдер — внешний, через `ILlmClient` (ADR-026): реализации OpenAI-совместимая (шлюзы) и нативная Anthropic;
  целевая модель — Claude. Тип клиента, base URL, ключ, модель — в настройках LLM.

## 7. Безопасность

- ASP.NET Core Identity: несколько пользователей, роли Teacher/Admin, блокировка после попыток,
  rate limiting на `/login`.
- Изоляция данных преподавателей — глобальный query filter EF Core по `teacher_id` (ADR-016).
- Hangfire Dashboard — только для администратора.
- Токены IMAP/SMTP/Диска/LLM — в переменных окружения или зашифрованы в `Setting` (Data Protection API).
- HTTPS — Caddy с автоматическим сертификатом.

## 8. Развёртывание

- `docker-compose.yml`: `app`, `db` (postgres:17), `caddy`; том для БД; ключи Data Protection в томе.
- Образ `app` включает `tesseract-ocr` и `tesseract-ocr-rus` (ADR-019).
- CI (GitHub Actions): build → test (с Testcontainers) → образ в GHCR.
- CD: по тегу — `ssh` на VPS, `docker compose pull && up -d`, миграции применяются при старте.
- Бэкап: ежедневный `pg_dump` в отдельную папку (опционально — на тот же Яндекс Диск).
