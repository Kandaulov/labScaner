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

        if name == "mobile":
            page.goto(f"{BASE}/")
            page.click("label[for=nav-toggle]")
            page.wait_for_timeout(400)
            page.screenshot(path=folder / "01-summary-menu.png")
        context.close()
    browser.close()
print(f"Снимки сохранены в {OUT}")
