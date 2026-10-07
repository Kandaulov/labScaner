# Развёртывание labScaner

Три контейнера: `app` (приложение), `db` (PostgreSQL 17), `caddy` (HTTPS и сжатие).
Данные — в томах Docker: `pgdata` (база), `keys` (ключи шифрования входа и токенов), `caddy_data` (сертификаты).

## Запуск

Нужны Docker и Docker Compose v2.

```bash
cd deploy
cp .env.example .env        # заполнить DB_PASSWORD и ADMIN_PASSWORD
docker compose up -d        # взять готовый образ из GitHub
# или
docker compose up -d --build  # собрать из исходников
```

Открыть `https://localhost` (или ваш домен), войти логином и паролем из `.env`.
Для `localhost` браузер предупредит о сертификате — Caddy выпускает его сам; это нормально.

## Сервер

1. В DNS направить домен на IP сервера, открыть порты 80 и 443.
2. В `.env` указать `DOMAIN=ваш.домен` — сертификат Let's Encrypt Caddy получит сам.
3. `docker compose pull && docker compose up -d`. Миграции БД применяются при старте приложения.

## Полезное

| Команда | Что делает |
|---|---|
| `docker compose ps` | Состояние контейнеров (у `app` должно быть `healthy`) |
| `docker compose logs -f app` | Журнал приложения |
| `docker compose exec db pg_dump -U labscaner labscaner > backup.sql` | Резервная копия БД |
| `docker compose down` | Остановить (данные в томах сохраняются) |

Образ публикует GitHub Actions при каждом изменении `main`: `ghcr.io/kandaulov/labscaner:main`
и `ghcr.io/kandaulov/labscaner:sha-<коммит>`. Ежедневный бэкап и обновление по тегу — этап 8.
