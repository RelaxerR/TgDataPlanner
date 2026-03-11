#!/bin/bash

# =============================================================================
# TgDataPlanner - Скрипт деплоя Telegram бота в Docker
# =============================================================================
# Использование:
#   ./deploy.sh build      - Сборка образа
#   ./deploy.sh up         - Запуск контейнера
#   ./deploy.sh down       - Остановка контейнера
#   ./deploy.sh restart    - Перезапуск контейнера
#   ./deploy.sh rebuild    - Полная пересборка и запуск
#   ./deploy.sh logs       - Просмотр логов
#   ./deploy.sh status     - Проверка статуса
#   ./deploy.sh update     - Обновление из git и пересборка
#   ./deploy.sh clean      - Очистка неиспользуемых ресурсов Docker
#   ./deploy.sh help       - Показать эту справку
# =============================================================================

set -e

# Цвета для вывода
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Конфигурация
PROJECT_NAME="tg-data-planner"
COMPOSE_FILE="docker-compose.yml"
SERVICE_NAME="tgbot"
GIT_REPO="https://github.com/RelaxerR/TgDataPlanner.git"

# =============================================================================
# Функции
# =============================================================================

print_header() {
    echo -e "${BLUE}=============================================${NC}"
    echo -e "${BLUE}$1${NC}"
    echo -e "${BLUE}=============================================${NC}"
}

print_success() {
    echo -e "${GREEN}✓ $1${NC}"
}

print_error() {
    echo -e "${RED}✗ $1${NC}"
}

print_warning() {
    echo -e "${YELLOW}⚠ $1${NC}"
}

print_info() {
    echo -e "${BLUE}ℹ $1${NC}"
}

check_docker() {
    if ! command -v docker &> /dev/null; then
        print_error "Docker не установлен!"
        exit 1
    fi
    
    if ! command -v docker compose &> /dev/null; then
        print_error "Docker Compose не установлен!"
        exit 1
    fi
    
    if ! docker info &> /dev/null; then
        print_error "Docker демон не запущен!"
        exit 1
    fi
    
    print_success "Docker готов к работе"
}

check_files() {
    if [ ! -f "$COMPOSE_FILE" ]; then
        print_error "Файл $COMPOSE_FILE не найден!"
        exit 1
    fi
    
    if [ ! -f "Dockerfile" ]; then
        print_error "Файл Dockerfile не найден!"
        exit 1
    fi
    
    print_success "Все необходимые файлы найдены"
}

build() {
    print_header "Сборка Docker образа"
    check_docker
    check_files
    
    print_info "Начинаем сборку образа..."
    docker compose build --progress=plain
    
    if [ $? -eq 0 ]; then
        print_success "Сборка завершена успешно"
    else
        print_error "Ошибка при сборке"
        exit 1
    fi
}

up() {
    print_header "Запуск контейнера"
    check_docker
    check_files
    
    print_info "Запускаем сервис $SERVICE_NAME..."
    docker compose up -d
    
    if [ $? -eq 0 ]; then
        print_success "Контейнер запущен"
        sleep 2
        status
    else
        print_error "Ошибка при запуске"
        exit 1
    fi
}

down() {
    print_header "Остановка контейнера"
    check_docker
    
    print_info "Останавливаем сервис..."
    docker compose down
    
    if [ $? -eq 0 ]; then
        print_success "Контейнер остановлен"
    else
        print_error "Ошибка при остановке"
        exit 1
    fi
}

restart() {
    print_header "Перезапуск контейнера"
    check_docker
    check_files
    
    print_info "Перезапускаем сервис..."
    docker compose restart $SERVICE_NAME
    
    if [ $? -eq 0 ]; then
        print_success "Контейнер перезапущен"
        sleep 2
        status
    else
        print_error "Ошибка при перезапуске"
        exit 1
    fi
}

rebuild() {
    print_header "Полная пересборка и запуск"
    check_docker
    check_files
    
    print_info "Останавливаем текущий контейнер..."
    docker compose down
    
    print_info "Собираем образ без кэша..."
    docker compose build --no-cache
    
    print_info "Запускаем контейнер..."
    docker compose up -d --force-recreate
    
    if [ $? -eq 0 ]; then
        print_success "Пересборка и запуск завершены"
        sleep 2
        status
    else
        print_error "Ошибка при пересборке"
        exit 1
    fi
}

logs() {
    print_header "Просмотр логов"
    check_docker
    
    print_info "Последние 100 строк логов (Ctrl+C для выхода)..."
    docker compose logs -f --tail=100 $SERVICE_NAME
}

status() {
    print_header "Статус сервиса"
    check_docker
    
    echo ""
    print_info "Контейнеры:"
    docker compose ps
    
    echo ""
    print_info "Использование ресурсов:"
    docker stats --no-stream $PROJECT_NAME-$SERVICE_NAME 2>/dev/null || print_warning "Контейнер не запущен"
    
    echo ""
}

update() {
    print_header "Обновление из репозитория"
    check_docker
    check_files
    
    if [ -d ".git" ]; then
        print_info "Обновляем код из репозитория..."
        git pull origin main
        
        if [ $? -eq 0 ]; then
            print_success "Код обновлен"
            rebuild
        else
            print_error "Ошибка при обновлении кода"
            exit 1
        fi
    else
        print_warning "Это не git-репозиторий. Пропускаем обновление кода."
        rebuild
    fi
}

clean() {
    print_header "Очистка ресурсов Docker"
    check_docker
    
    print_warning "Это удалит все неиспользуемые образы, контейнеры и тома!"
    read -p "Продолжить? (y/n): " confirm
    
    if [ "$confirm" = "y" ] || [ "$confirm" = "Y" ]; then
        print_info "Очищаем неиспользуемые ресурсы..."
        docker system prune -f
        docker builder prune -f
        print_success "Очистка завершена"
    else
        print_info "Очистка отменена"
    fi
}

show_help() {
    echo ""
    echo "TgDataPlanner Deployment Script"
    echo ""
    echo "Использование: ./deploy.sh <команда>"
    echo ""
    echo "Команды:"
    echo "  build     - Сборка Docker образа"
    echo "  up        - Запуск контейнера"
    echo "  down      - Остановка контейнера"
    echo "  restart   - Перезапуск контейнера"
    echo "  rebuild   - Полная пересборка и запуск"
    echo "  logs      - Просмотр логов в реальном времени"
    echo "  status    - Проверка статуса контейнера"
    echo "  update    - Обновление из git и пересборка"
    echo "  clean     - Очистка неиспользуемых ресурсов Docker"
    echo "  help      - Показать эту справку"
    echo ""
    echo "Примеры:"
    echo "  ./deploy.sh up          # Запустить бота"
    echo "  ./deploy.sh logs        # Смотреть логи"
    echo "  ./deploy.sh update      # Обновить и пересобрать"
    echo ""
}

# =============================================================================
# Основная логика
# =============================================================================

case "${1:-}" in
    build)
        build
        ;;
    up)
        up
        ;;
    down)
        down
        ;;
    restart)
        restart
        ;;
    rebuild)
        rebuild
        ;;
    logs)
        logs
        ;;
    status)
        status
        ;;
    update)
        update
        ;;
    clean)
        clean
        ;;
    help|--help|-h)
        show_help
        ;;
    *)
        print_error "Неизвестная команда: ${1:-}"
        echo ""
        show_help
        exit 1
        ;;
esac