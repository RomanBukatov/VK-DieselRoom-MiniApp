#!/usr/bin/env bash
# Очистка базы после приёмки, перед запуском для настоящих гостей.
#
#   scripts/reset-acceptance-data.sh --dry-run          только показать, что будет удалено
#   scripts/reset-acceptance-data.sh                    резервная копия, вопрос-подтверждение, очистка
#   scripts/reset-acceptance-data.sh --max-guests 200   поднять предохранитель (по умолчанию 50 гостей)
#
# Удаляет гостей, баллы, купоны, визиты, брони, итоги недель и журнал аудита.
# Сохраняет афишу, меню, каталог с фото, ставки, уровни, достижения и сотрудников; остатки подарков восстанавливает.
# Запускать из любого места на сервере, рядом с docker-compose.yml и .env.
set -euo pipefail

cd "$(dirname "$0")/.."

DRY_RUN=0
MAX_GUESTS=50
while [ $# -gt 0 ]; do
  case "$1" in
    --dry-run) DRY_RUN=1 ;;
    --max-guests) MAX_GUESTS="${2:?Укажите число после --max-guests}"; shift ;;
    *) echo "Неизвестный параметр: $1" >&2; exit 2 ;;
  esac
  shift
done

# Учётные данные берём внутри контейнера, чтобы не разбирать .env и не показывать пароль
db() { docker compose exec -T diesel-db sh -c "$1"; }
psql_query() { db 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 -At' <<<"$1"; }

if [ -z "$(docker compose ps --status running -q diesel-db)" ]; then
  echo "База не запущена: сначала docker compose up -d diesel-db" >&2
  exit 1
fi

summary() {
  psql_query 'SELECT format(E'\''  гостей: %s\n  операций с баллами: %s\n  купонов: %s\n  визитов: %s\n  броней: %s\n  записей журнала аудита: %s'\'',
    (SELECT count(*) FROM "Users"), (SELECT count(*) FROM "PointsTransactions"), (SELECT count(*) FROM "ShopOrders"),
    (SELECT count(*) FROM "Visits"), (SELECT count(*) FROM "Bookings"), (SELECT count(*) FROM "StaffAuditLogs"));'
}

echo "Будет удалено:"
summary
GUESTS="$(psql_query 'SELECT count(*) FROM "Users";')"

if [ "$DRY_RUN" -eq 1 ]; then
  echo "Режим --dry-run: ничего не изменено."
  exit 0
fi

# Предохранитель: после запуска здесь будут настоящие гости и их баллы
if [ "$GUESTS" -gt "$MAX_GUESTS" ]; then
  echo "ОТКАЗ: гостей $GUESTS, это больше $MAX_GUESTS. Похоже, программа уже работает с настоящими гостями." >&2
  echo "Если это всё ещё приёмка, запустите с --max-guests $GUESTS." >&2
  exit 1
fi

printf 'Введите RESET, чтобы продолжить: '
read -r ANSWER || ANSWER=""
if [ "$ANSWER" != "RESET" ]; then
  echo "Отменено: ничего не изменено."
  exit 1
fi

# Останавливаем API, иначе гость или фоновая задача запишут данные прямо во время очистки
API_WAS_RUNNING=""
if [ -n "$(docker compose ps --status running -q diesel-api)" ]; then
  API_WAS_RUNNING=1
  docker compose stop diesel-api
fi
restart_api() { [ -z "$API_WAS_RUNNING" ] || docker compose start diesel-api; }
trap restart_api EXIT

mkdir -p backups
BACKUP="backups/before-reset-$(date +%Y%m%d-%H%M%S).dump"
db 'pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc' >"$BACKUP"
# Без проверенной копии не удаляем ничего
db 'pg_restore -l >/dev/null' <"$BACKUP" || { echo "ОТКАЗ: резервная копия не читается: $BACKUP" >&2; exit 1; }
echo "Резервная копия: $BACKUP"

db 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -q -v ON_ERROR_STOP=1' <scripts/reset-acceptance-data.sql

echo "Готово. Сейчас в базе:"
summary
echo "Вернуть всё как было:"
echo "  docker compose stop diesel-api"
echo "  docker compose exec -T diesel-db sh -c 'pg_restore -U \"\$POSTGRES_USER\" -d \"\$POSTGRES_DB\" --clean --if-exists' < $BACKUP"
echo "  docker compose start diesel-api"
