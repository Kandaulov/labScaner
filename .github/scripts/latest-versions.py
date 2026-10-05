#!/usr/bin/env python3
"""Сравнивает версии из Directory.Packages.props с последними стабильными на nuget.org.
Результат — аннотации GitHub Actions (::notice), чтобы видеть их без логов."""
import json
import re
import urllib.request
import xml.etree.ElementTree as ET

def parse(v):
    return tuple(int(x) for x in re.findall(r"\d+", v.split("-")[0])[:4])

root = ET.parse("Directory.Packages.props").getroot()
packages = {e.get("Include"): e.get("Version") for e in root.iter("PackageVersion")}
tools = json.load(open(".config/dotnet-tools.json"))["tools"]
packages.update({k: v["version"] for k, v in tools.items()})

for name, current in sorted(packages.items()):
    url = f"https://api.nuget.org/v3-flatcontainer/{name.lower()}/index.json"
    try:
        versions = json.load(urllib.request.urlopen(url, timeout=20))["versions"]
    except Exception as exc:  # noqa: BLE001
        print(f"::warning title=Версия пакета::{name}: не удалось получить ({exc})")
        continue
    stable = [v for v in versions if "-" not in v]
    latest = max(stable, key=parse) if stable else versions[-1]
    mark = "актуальна" if parse(latest) <= parse(current) else f"есть {latest}"
    print(f"::notice title=Версия пакета::{name} {current} — {mark}")
