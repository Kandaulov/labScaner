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

        # Прототип журнала (шаг 2.5): сценарии с htmx
        import time
        started = time.perf_counter()
        response = page.goto(f"{BASE}/Prototype/Journal")
        page.wait_for_load_state("networkidle")
        elapsed = (time.perf_counter() - started) * 1000
        size = len(response.body()) // 1024
        print(f"::notice title=Журнал ({name})::загрузка {elapsed:.0f} мс, HTML {size} КБ")
        page.screenshot(path=folder / "20-journal.png", full_page=name == "desktop")

        page.click("#c4-4 .cell")
        page.wait_for_selector("#c4-4 .quick")
        page.screenshot(path=folder / "21-journal-quick.png")

        started = time.perf_counter()
        page.click("#c4-4 .quick button[value='5']")
        page.wait_for_selector("#c4-4 .cell--ok")
        print(f"::notice title=Быстрая отметка ({name})::{(time.perf_counter() - started) * 1000:.0f} мс до обновления ячейки")
        page.screenshot(path=folder / "22-journal-marked.png")

        page.click("#c9-3 .cell")
        page.wait_for_selector("#c9-3 .quick")
        page.click("#c9-3 .quick a")
        page.wait_for_selector("#panel .panel")
        page.screenshot(path=folder / "23-journal-panel.png", full_page=name == "mobile")

        page.click("#panel button[role=tab]:nth-child(2)")
        page.wait_for_selector("#panel .timeline")
        page.screenshot(path=folder / "24-journal-comments.png", full_page=name == "mobile")
        page.click("#panel [data-close-panel]")

        page.goto(f"{BASE}/Prototype/Journal?filter=automat")
        page.wait_for_load_state("networkidle")
        for box in page.query_selector_all(".jt__check")[:2]:
            box.check()
        page.query_selector_all("tbody .jt__check")[0].dispatch_event("change")
        page.wait_for_selector("#selection button")
        page.click("#selection button")
        page.wait_for_selector("#modal .modal")
        page.screenshot(path=folder / "25-journal-automat-dialog.png")
        page.keyboard.press("Escape")

        page.goto(f"{BASE}/Prototype/Journal")
        page.click("#e3 .final")
        page.wait_for_selector("#e3 .quick")
        page.screenshot(path=folder / "26-journal-exam.png")

        if name == "mobile":
            page.goto(f"{BASE}/")
            page.click("label[for=nav-toggle]")
            page.wait_for_timeout(400)
            page.screenshot(path=folder / "01-summary-menu.png")
        context.close()
    browser.close()
print(f"Снимки сохранены в {OUT}")
