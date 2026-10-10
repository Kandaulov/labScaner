#!/usr/bin/env python3
"""Снимки экранов запущенного приложения для просмотра без локального запуска.
Использование: screenshots.py <base_url> <out_dir>; логин и пароль администратора — в переменных
SHOT_LOGIN, SHOT_PASSWORD."""
import os
import sys
from pathlib import Path

from playwright.sync_api import sync_playwright

BASE, OUT = sys.argv[1].rstrip("/"), Path(sys.argv[2])
LOGIN, PASSWORD = os.environ["SHOT_LOGIN"], os.environ["SHOT_PASSWORD"]
PAGES = [
    ("01-summary", "/"),
    ("02-journal", "/Journal"),
    ("03-processing", "/Processing"),
    ("04-unrecognized", "/Unrecognized"),
    ("05-notifications", "/Notifications"),
    ("06-events", "/Events"),
    ("07-subjects", "/Subjects"),
    ("08-groups", "/Groups"),
    ("09-settings", "/Settings"),
    ("10-admin", "/Admin"),
    ("11-admin-users", "/Admin/Users"),
    ("12-admin-calendar", "/Admin/Calendar"),
    ("13-password", "/Settings/Password"),
]
VIEWPORTS = {"desktop": {"width": 1440, "height": 900}, "mobile": {"width": 390, "height": 844}}


def login(page, password):
    page.goto(f"{BASE}/Account/Login")
    if page.locator("input[name='Input.Login']").count() == 0:
        page.screenshot(path=OUT / "failure.png", full_page=True)
        text = page.inner_text("body")[:1500].replace("\n", " | ")
        raise RuntimeError(f"Нет формы входа на {page.url}: {text}")
    page.fill("input[name='Input.Login']", LOGIN)
    page.fill("input[name='Input.Password']", password)
    page.click("button[type=submit]")
    page.wait_for_load_state("networkidle")


with sync_playwright() as p:
    browser = p.chromium.launch()
    for name, viewport in VIEWPORTS.items():
        folder = OUT / name
        folder.mkdir(parents=True, exist_ok=True)
        context = browser.new_context(viewport=viewport, locale="ru-RU", device_scale_factor=1)
        page = context.new_page()

        page.goto(f"{BASE}/Account/Login")
        page.screenshot(path=folder / "00-login.png")
        login(page, "wrong-password")
        page.screenshot(path=folder / "00-login-error.png")

        login(page, PASSWORD)
        for file, path in PAGES:
            page.goto(f"{BASE}{path}")
            page.wait_for_load_state("networkidle")
            page.screenshot(path=folder / f"{file}.png", full_page=True)

        # Шаг 3.1: добавить преподавателя (временный пароль) и семестр
        page.goto(f"{BASE}/Admin/Users")
        page.fill("input[name='NewUser.Login']", f"petrov{name}")
        page.fill("input[name='NewUser.DisplayName']", "Петров П. П.")
        page.click("form[action*='Create'] button[type=submit]")
        page.wait_for_load_state("networkidle")
        page.screenshot(path=folder / "14-admin-user-created.png", full_page=True)

        page.goto(f"{BASE}/Admin/Calendar")
        page.select_option("select[name='NewTerm.StartYear']", "2026")
        page.select_option("select[name='NewTerm.Season']", "Autumn")
        page.fill("input[name='NewTerm.CreditWeekStart']", "2026-12-21")
        page.fill("input[name='NewTerm.SessionStart']", "2027-01-11")
        page.click("form[action*='Add'] button[type=submit]")
        page.wait_for_load_state("networkidle")
        page.screenshot(path=folder / "15-admin-calendar-added.png", full_page=True)

        # Шаг 3.2: импорт групп из Excel и справочник
        if name == "desktop":
            page.goto(f"{BASE}/Groups/Import")
            page.set_input_files("input[type=file]", os.environ["SHOT_GROUPS_XLSX"])
            page.click("form[action*='Preview'] button[type=submit]")
            page.wait_for_load_state("networkidle")
            page.screenshot(path=folder / "16-groups-import-preview.png", full_page=True)
            page.click("form[action*='Apply'] .import-summary button[type=submit]")
            page.wait_for_load_state("networkidle")
            page.screenshot(path=folder / "17-groups-import-done.png")
        page.goto(f"{BASE}/Groups")
        page.wait_for_load_state("networkidle")
        page.screenshot(path=folder / "18-groups.png", full_page=True)

        # Шаг 3.3: предмет
        if name == "desktop":
            page.goto(f"{BASE}/Subjects")
            page.fill("input[name='Input.Name']", "Корпоративные информационные системы")
            page.fill("input[name='Input.Code']", "КорпИС")
            page.fill("input[name='Input.Aliases']", "КИС, КорпИнфСист")
            page.fill("textarea[name='Input.AiReferenceText']", "Нотации: BPMN 2.0, IDEF0. Отчёт: титульный лист, цель, ход работы, выводы.")
            page.fill("input[name='Input.DiskPathTemplate']", "30 Политех/03 {Код}/10 {Код} - Отчетные документы/{Учебный год} {Код} {Направление} - {N} сем")
            page.wait_for_timeout(600)
            page.click("form[action*='Save'] .card__header button[type=submit]")
            page.wait_for_load_state("networkidle")
            page.goto(f"{BASE}/Subjects?new=true")
            page.fill("input[name='Input.Name']", "Операционные системы")
            page.fill("input[name='Input.Code']", "ОС")
            page.select_option("select[name='Input.FinalAssessment']", "Pass")
            page.click("form[action*='Save'] .card__header button[type=submit]")
            page.wait_for_load_state("networkidle")
            page.goto(f"{BASE}/Subjects")
        else:
            page.goto(f"{BASE}/Subjects")
        page.wait_for_load_state("networkidle")
        page.screenshot(path=folder / "19-subjects.png", full_page=True)

        # Шаг 3.4: предмет в семестре, темы курсовых, журнал группы
        if name == "desktop":
            page.goto(f"{BASE}/Subjects")
            page.wait_for_load_state("networkidle")
            form = "form[action*='AddTerm']"
            page.fill(f"{form} input[name='NewTerm.StudySemester']", "7")
            page.fill(f"{form} input[name='NewTerm.Labs']", "8")
            page.check(f"{form} input[name='NewTerm.Coursework']")
            for box in page.query_selector_all(f"{form} input[name='NewTerm.GroupIds']"):
                box.check()
            page.click(f"{form} button[type=submit]")
            page.wait_for_load_state("networkidle")
            page.screenshot(path=folder / "20-subject-term.png", full_page=True)

            page.set_input_files("form[action*='Topics'] input[type=file]", os.environ["SHOT_GROUPS_XLSX"])
            page.click("form[action*='Topics'] button[type=submit]")
            page.wait_for_load_state("networkidle")
            page.screenshot(path=folder / "21-subject-term-topics.png", full_page=True)

            # Шаг 3.5: задания из DOCX — предпросмотр деления, применение, задание лабы, курсовая
            term_url = page.url
            page.set_input_files("form[action*='UploadLabs'] input[type=file]", os.environ["SHOT_LABS_DOCX"])
            page.click("form[action*='UploadLabs'] button[type=submit]")
            page.wait_for_load_state("networkidle")
            page.screenshot(path=folder / "26-tasks-preview.png", full_page=True)
            page.click("form[action*='Apply'] button[type=submit]")
            page.wait_for_load_state("networkidle")
            page.set_input_files("form[action*='UploadCoursework'] input[type=file]", os.environ["SHOT_COURSEWORK_DOCX"])
            page.click("form[action*='UploadCoursework'] button[type=submit]")
            page.wait_for_load_state("networkidle")
            page.screenshot(path=folder / "27-term-tasks.png", full_page=True)
            page.click("a[href^='/Subjects/Assignment'] >> nth=3")
            page.wait_for_load_state("networkidle")
            page.screenshot(path=folder / "28-assignment.png", full_page=True)
            page.goto(term_url)

            page.goto(f"{BASE}/Subjects")
            page.wait_for_load_state("networkidle")
            page.screenshot(path=folder / "22-subjects-terms.png", full_page=True)

        # Шаг 3.6а: почта — пустая форма и ошибка подключения (почтового сервера в CI нет)
        page.goto(f"{BASE}/Settings/Mail")
        page.wait_for_load_state("networkidle")
        page.screenshot(path=folder / "29-mail-empty.png", full_page=True)
        if name == "desktop":
            page.fill("input[name='Input.Address']", "v.ivanov@ulstu.ru")
            page.fill("input[name='Input.Password']", "app-password")
            page.fill("input[name='Input.ImapHost']", "imap.example.invalid")
            page.fill("input[name='Input.SmtpHost']", "smtp.example.invalid")
            page.click("form[action*='Save'] .card__header button[type=submit]")
            page.wait_for_load_state("networkidle")
            page.screenshot(path=folder / "30-mail-error.png", full_page=True)

        import time
        started = time.perf_counter()
        response = page.goto(f"{BASE}/Journal")
        page.wait_for_load_state("networkidle")
        elapsed = (time.perf_counter() - started) * 1000
        size = len(response.body()) // 1024
        print(f"::notice title=Журнал ({name})::загрузка {elapsed:.0f} мс, HTML {size} КБ")
        page.screenshot(path=folder / "23-journal.png", full_page=True)

        if name == "desktop" and page.locator("select[name='group'] option").count() > 1:
            page.select_option("select[name='group']", index=1)
            page.wait_for_selector("#journal-page .jt")
            page.wait_for_load_state("networkidle")
            page.screenshot(path=folder / "24-journal-group2.png", full_page=True)

        if page.locator(".topic").count() > 0:
            page.click(".topic >> nth=0")
            page.wait_for_selector(".topic-edit input")
            page.fill(".topic-edit input", "Учёт заявок в сервисном центре")
            page.click(".topic-edit button[type=submit]")
            page.wait_for_selector(".topic-edit", state="detached")
            page.screenshot(path=folder / "25-journal-topic.png")

        if name == "mobile":
            page.goto(f"{BASE}/")
            page.click("label[for=nav-toggle]")
            page.wait_for_timeout(400)
            page.screenshot(path=folder / "01-summary-menu.png")
        context.close()
    browser.close()
print(f"Снимки сохранены в {OUT}")
