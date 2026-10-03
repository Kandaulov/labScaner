# Технический паспорт labScaner

| Файл | Что это |
|---|---|
| `labScaner - Технический паспорт <дата>.docx` | Паспорт на корпоративном шаблоне (ГОСТ Р 59795-2021, п. 5.8) |
| `labScaner - Технический паспорт <дата>.pdf` | То же для просмотра |
| `passport.md` | Исходник разметки — правки вносить сюда |
| `arch.dot`, `pipe.dot` | Исходники схем (Graphviz); `arch.png`, `pipe.png` — отрисованные схемы |

Сборка: навык `aisgorod-docx-template`.

```bash
dot -Tpng arch.dot -o arch.png && dot -Tpng pipe.dot -o pipe.png
python3 build_docx.py passport.md "labScaner - Технический паспорт <дата>.docx"
python3 check_docx.py "labScaner - Технический паспорт <дата>.docx"
```

При изменении `DECISIONS.md` / `ARCHITECTURE.md` паспорт обновляется в том же коммите.
После правки DOCX в Word: Ctrl+A, F9 — обновить оглавление, номера и ссылки.
