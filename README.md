# BrokerPilot Market Data Aggregator

Тестовое задание на разработку системы сбора, обработки и хранения биржевых котировок в реальном времени

Решение включает:

- три независимых WebSocket-имитатора бирж с разными форматами сообщений;
- сервер-агрегатор, одновременно подключающийся ко всем источникам;
- нормализацию котировок к единой модели;
- потокобезопасную дедупликацию;
- bounded-очередь с backpressure;
- пакетную запись в PostgreSQL;
- повторные попытки записи и dead-letter fallback при недоступности БД;
- автоматическое переподключение с exponential backoff;
- idle-timeout для обнаружения зависшего соединения;
- graceful shutdown с drain уже принятых тиков;
- метрики, логирование и тесты проблемных сценариев.

Стек: **.NET 8 / C# 12, ASP.NET Core, Npgsql, PostgreSQL 16, Docker Compose, xUnit, FluentAssertions, NSubstitute**

---

## 1. Быстрый запуск

### Требования

Для запуска всего стенда нужен:

- Docker Desktop;
- Docker Compose v2

Для локальной сборки и запуска из Rider дополнительно нужен .NET 8 SDK

### Запуск через Docker Compose

Из корня репозитория:

```text
docker compose up --build
```

После запуска доступны:

| Компонент | Адрес |
|---|---|
| Aggregator | `http://localhost:7200` |
| Alpha WebSocket | `ws://localhost:7101/ws` |
| Beta WebSocket | `ws://localhost:7102/ws` |
| Gamma WebSocket | `ws://localhost:7103/ws` |
| PostgreSQL | `localhost:5432` |

Проверить состояние агрегатора:

```text
http://localhost:7200/health
```

Посмотреть метрики:

```text
http://localhost:7200/metrics
```

Остановить стенд:

```text
docker compose down
```

Если необходимо пересоздать PostgreSQL вместе со схемой и очистить volume:

```text
docker compose down -v
docker compose up --build
```

PostgreSQL выполняет init-скрипты только при создании пустого volume

### Запуск тестов

```text
dotnet restore BrokerPilot.MarketData.sln
dotnet build BrokerPilot.MarketData.sln -c Release
dotnet test BrokerPilot.MarketData.sln -c Release --no-build
```

### Запуск из Rider

1. Открыть `BrokerPilot.MarketData.sln`
2. Поднять PostgreSQL:

   ```text
   docker compose up -d postgres
   ```

3. Запустить одновременно проекты:
   - `BrokerPilot.ExchangeSimulator.Alpha`;
   - `BrokerPilot.ExchangeSimulator.Beta`;
   - `BrokerPilot.ExchangeSimulator.Gamma`;
   - `BrokerPilot.MarketData.Aggregator`
4. Для ручной проверки сценариев можно использовать готовые запросы из `http/marketdata.http`

Docker Compose является основным воспроизводимым способом запуска решения

---

## 2. Архитектура

Агрегатор поддерживает отдельное WebSocket-подключение к каждой бирже. Полученные сообщения сначала преобразуются в единый внутренний формат, затем проходят дедупликацию и помещаются в ограниченный канал. Отдельный consumer собирает тики в батчи и записывает их в PostgreSQL. Если запись в БД после повторных попыток не удалась, данные сохраняются в dead-letter файл

Структура solution:

```text
src/
  BrokerPilot.MarketData.Domain
  BrokerPilot.MarketData.Application
  BrokerPilot.MarketData.Infrastructure
  BrokerPilot.MarketData.Aggregator
  Simulators/
    BrokerPilot.ExchangeSimulator.Common
    BrokerPilot.ExchangeSimulator.Alpha
    BrokerPilot.ExchangeSimulator.Beta
    BrokerPilot.ExchangeSimulator.Gamma

tests/
  BrokerPilot.MarketData.Tests
```

### Domain

Содержит внутреннюю модель нормализованного тика и построение детерминированного идентификатора тика

Слой не зависит от транспорта, PostgreSQL и ASP.NET Core

### Application

Содержит контракты и основную потоковую логику:

- дедупликацию;
- bounded queue;
- batch consumer;
- метрики;
- lifecycle агрегатора;
- интерфейсы источников и persistence

### Infrastructure

Содержит адаптеры внешнего мира:

- WebSocket-клиент;
- reconnect/backoff;
- парсеры форматов бирж;
- PostgreSQL repository;
- retry/dead-letter strategy

### Aggregator

Composition root приложения:

- конфигурация и DI;
- регистрация hosted services;
- `/health`;
- `/metrics`;
- согласование времени graceful shutdown

---

## 3. Имитаторы бирж

В решении три WebSocket-сервера. Форматы отличаются не только именами полей, но и типами и форматом времени

### Alpha

```text
{
  "symbol": "EURUSD",
  "price": "1.08525",
  "volume": 120000,
  "timestamp": "2026-06-01T12:00:00.123Z"
}
```

Особенности:

- цена — строка;
- время — ISO 8601

### Beta

```text
{
  "s": "EURUSD",
  "p": 1.08525,
  "q": "120000",
  "ts": 1780315200123
}
```

Особенности:

- короткие имена полей;
- цена — число;
- объём — строка;
- время — Unix milliseconds.

### Gamma

```text
{
  "instrument": {
    "ticker": "EURUSD"
  },
  "last": 1.08525,
  "size": 120000,
  "time": "20260601 12:00:00.123"
}
```

Особенности:

- тикер находится во вложенном объекте;
- собственные имена полей;
- собственный строковый формат времени

### Управление сбоями симуляторов

Имитаторы поддерживают команды для ручной проверки устойчивости агрегатора

Разорвать все текущие WebSocket-соединения Alpha:

```text
POST http://localhost:7101/admin/disconnect
```

Включить/выключить периодические дубликаты:

```text
POST http://localhost:7101/admin/duplicates/true
POST http://localhost:7101/admin/duplicates/false
```

Остановить отправку данных, не закрывая соединение:

```text
POST http://localhost:7101/admin/pause/true
POST http://localhost:7101/admin/pause/false
```

Аналогичные endpoints доступны на портах Beta и Gamma

---

## 4. Ключевые инженерные решения

### 4.1. Независимая обработка источников

Для каждой биржи создаётся отдельный `IExchangeFeed` со своей долгоживущей async-задачей

Ошибка, disconnect или reconnect одного источника обрабатываются внутри его собственного цикла и не останавливают остальные источники

Если feed неожиданно завершается из-за необработанной программной ошибки, приложение останавливается явно вместо незаметной работы с потерянным источником

### 4.2. Переподключение

WebSocket feed работает в цикле до отмены приложения

При обрыве используется exponential backoff:

- начальная задержка: 250 ms;
- экспоненциальный рост;
- максимальная задержка: 10 s;
- jitter: ±20%.

Backoff сбрасывается только после стабильной работы соединения в течение `ReconnectStabilityThreshold` — 30 секунд. Это защищает от плотного reconnect loop, если источник принимает соединение и почти сразу снова его рвёт

При штатной остановке агрегатор инициирует WebSocket close handshake. Симулятор параллельно с отправкой котировок читает входящие WebSocket frames, обрабатывает Close frame и отвечает через CloseOutputAsync. Close handshake ограничен таймаутом, после которого соединение может быть принудительно завершено

### 4.3. Обнаружение зависшего соединения

Каждый `ReceiveAsync` ограничен `IdleTimeout`

Значение по умолчанию — 5 секунд

Если сокет формально остаётся открытым, но данные перестают поступать, feed считает соединение зависшим и создаёт новое подключение

### 4.4. Нормализация

Каждый формат биржи реализует собственный `IExchangeMessageParser`

После парсинга все сообщения преобразуются в единую модель:

```text
Id
Symbol
Price
Volume
Timestamp
Source
ReceivedAt
```

Добавление новой биржи требует только:

1. добавить новый parser, реализующий `IExchangeMessageParser`;
2. зарегистрировать его в DI;
3. добавить источник в `MarketData:Sources`.

Существующие parsers, очередь, дедупликатор и persistence при этом не меняются

### 4.5. Дедупликация

Дубликатом считается тик с одинаковыми нормализованными полями:

```text
source + symbol + price + volume + timestamp
```

`source` входит в ключ намеренно: одинаковые котировки с двух разных бирж не должны считаться одним событием

Из ключевых полей строится SHA-256, первые 128 бит которого используются как детерминированный `Guid` (`TickIdentity`)

Окно in-memory дедупликации — **2 минуты**

Дедупликатор использует `ConcurrentDictionary` и атомарные `TryAdd` / `TryUpdate` для конкретного ключа. TTL измеряется через монотонный `TimeProvider.GetTimestamp()`, поэтому изменение системных часов не меняет фактическое deduplication window

Очистка старых ключей выполняется периодически и защищена single-flight guard, чтобы несколько конкурентных полных cleanup не выполнялись одновременно

При 1000 уникальных тиков/сек двухминутное окно соответствует примерно 120 000 активных ключей, то есть память ограничена выбранным временным окном, а не растёт бесконечно

### 4.6. Дополнительная идемпотентность в PostgreSQL

Тот же детерминированный `Id` используется как `PRIMARY KEY` в PostgreSQL

Запись выполняется с:

```text
ON CONFLICT (id) DO NOTHING
```

Это защищает от повторной записи после неоднозначного результата сетевого сбоя: если PostgreSQL уже закоммитил батч, а клиент получил ошибку и повторил запрос, строки не продублируются

### 4.7. Backpressure

Между ingestion и persistence используется bounded `Channel<NormalizedTick>`

Параметры по умолчанию:

- capacity: 20 000;
- `FullMode = Wait`;
- несколько producers;
- один consumer

Если БД пишет медленнее входного потока, producers ждут свободного места вместо создания неограниченной очереди в памяти

Trade-off выбран сознательно: при перегрузке лучше замедлить чтение источников и передать давление назад по TCP, чем допустить неограниченный рост RAM

### 4.8. Батчинг записи в PostgreSQL

Batch consumer отправляет в БД:

- до 500 тиков за один batch;
- либо неполный batch после 250 ms с момента получения первого элемента

`PostgresTickRepository` формирует один multi-row `INSERT` на batch

При потоке порядка 1000 тиков/сек это сокращает число DB round-trips с сотен/тысяч запросов до нескольких запросов в секунду

EF Core для hot path намеренно не используется: здесь нет сложной object graph/change tracking логики, а прямой Npgsql делает высокочастотную вставку прозрачнее и дешевле

### 4.9. Поведение при ошибке БД

Ошибки записи делятся на транзиентные и постоянные

Для транзиентной ошибки применяется retry:

- первая попытка;
- до 4 повторов;
- задержки 250 ms → 500 ms → 1 s → 2 s

После исчерпания retry batch сохраняется в persistent NDJSON dead-letter storage

Постоянные ошибки не повторяются бессмысленно и сразу переводят batch в dead-letter

Таким образом уже принятый тик заканчивает обработку одним из явных результатов:

- записан в PostgreSQL;
- сохранён в dead-letter;
- не удалось записать ни в БД, ни в dead-letter — `Critical` log и увеличение `TicksDropped`

Исключения записи не проглатываются молча

Dead-letter directory при Docker-запуске смонтирован на хост:

```text
data/dead-letter
```

### 4.10. Graceful shutdown

При штатной остановке выполняется следующая последовательность:

1. прекращается приём новых тиков от WebSocket-источников;
2. ожидается завершение producer-задач;
3. writer bounded channel закрывается;
4. batch consumer дочитывает уже принятые элементы;
5. последний неполный batch дописывается;
6. drain ограничен разумным таймаутом

Настройки:

```text
DrainTimeout                  15 s
ShutdownHeadroom              10 s
HostOptions.ShutdownTimeout   25 s
Docker stop_grace_period      35 s
```

Если drain не успевает завершиться в отведённый срок, необработанные тики считаются явно через `TicksDropped` и фиксируются `Critical`-логом

### 4.11. Управление async-задачами и CancellationToken

В pipeline нет `.Result`, `.Wait()`, `async void` и неконтролируемых fire-and-forget задач

Долгоживущие producer/consumer задачи принадлежат orchestrator-у, сохраняются и явно наблюдаются

Отмена ingestion и отмена writer-а разделены, чтобы graceful shutdown мог сначала остановить поступление новых данных, а затем выполнить drain очереди

---

## 5. Мониторинг

Основные события логируются:

- подключение и отключение WebSocket;
- reconnect;
- ошибки парсинга;
- ошибки БД;
- dead-letter fallback;
- forced shutdown;
- неожиданные ошибки фоновых задач

`GET /metrics` возвращает:

- `RawTicksReceived`;
- `TicksAccepted`;
- `DuplicateTicks`;
- `InvalidTicks`;
- `TicksWritten`;
- `DatabaseConflicts`;
- `DatabaseWriteAttemptFailures`;
- `TicksDeadLettered`;
- `TicksDropped`;
- `ReconnectsScheduled`;
- состояния источников;
- `queueDepth`;
- `queueCapacity`;
- `queueFillRatio`;
- количество ключей в дедупликаторе

Кроме HTTP endpoint, агрегатор раз в 10 секунд пишет snapshot метрик в лог

Для тестового задания метрики хранятся in-memory. Для production-системы логичным продолжением было бы подключение OpenTelemetry/Prometheus

---

## 6. Проверка сценариев из задания

### 6.1. Умеренная нагрузка

Каждый из трёх симуляторов настроен на 250 тиков/сек

Суммарная целевая нагрузка — около **750 тиков/сек**, то есть внутри требуемого диапазона 500–1000 тиков/сек

После запуска:

```text
docker compose up --build
```

наблюдать:

```text
GET http://localhost:7200/metrics
```

Ожидаемо:

- `RawTicksReceived` постоянно растёт;
- `TicksWritten` растёт вслед за принятыми тиками;
- `queueDepth` не растёт бесконечно;
- `TicksDropped` остаётся 0 при нормальной работе БД

### 6.2. Обрыв одного источника

```text
POST http://localhost:7101/admin/disconnect
```

Ожидаемо:

- Alpha отключается;
- агрегатор планирует reconnect;
- Beta и Gamma продолжают обрабатываться;
- Alpha переподключается автоматически;
- `ReconnectsScheduled` увеличивается

### 6.3. Дубликаты после reconnect

Симуляторы хранят небольшой буфер последних сообщений и повторно отправляют часть тиков при новом подключении

После disconnect/reconnect:

- replay приходит в агрегатор;
- `DuplicateTicks` увеличивается;
- повторный тик не попадает в PostgreSQL как новая строка

Периодические дубликаты можно дополнительно включить вручную:

```text
POST http://localhost:7101/admin/duplicates/true
```

### 6.4. Зависшее соединение

```text
POST http://localhost:7101/admin/pause/true
```

WebSocket остаётся открытым, но Alpha прекращает отправку данных

После `IdleTimeout` агрегатор считает соединение зависшим и переподключается. Beta и Gamma продолжают работу

Вернуть поток:

```text
POST http://localhost:7101/admin/pause/false
```

### 6.5. Недоступность PostgreSQL

Остановить БД:

```text
docker compose stop postgres
```

Ожидаемое поведение:

1. writer получает ошибки записи;
2. транзиентные ошибки повторяются с backoff;
3. после исчерпания retry batch сохраняется в `data/dead-letter`;
4. растут `DatabaseWriteAttemptFailures` и `TicksDeadLettered`;
5. ошибка не теряется молча

Вернуть БД:

```text
docker compose start postgres
```

Автоматический replay dead-letter файлов намеренно не реализован и указан в ограничениях

### 6.6. Graceful shutdown

Для изолированной проверки graceful shutdown останавливается только агрегатор:

```text
docker compose stop aggregator
```
Ожидаемое поведение:

1. агрегатор прекращает принимать новые тики;
2. WebSocket feeds корректно завершаются;
3. bounded channel закрывается для новых записей;
4. batch consumer дочитывает уже принятые тики;
5. последний неполный batch записывается в PostgreSQL;
6. процесс завершается после drain либо после истечения DrainTimeout

PostgreSQL и симуляторы при такой проверке продолжают работать, поэтому агрегатор может сохранить накопленные данные перед завершением

Повторно запустить агрегатор:

docker compose start aggregator

Полностью остановить стенд:

docker compose down

---

## 7. PostgreSQL

Схема создаётся скриптом:

```text
docker/postgres/001_init.sql
```

Основная таблица:

```text
market_ticks
```

Ключ:

```text
id uuid PRIMARY KEY
```

Индексы:

- `(source, symbol, event_time DESC)`;
- `(event_time DESC)`

Данные тика хранятся в нормализованном виде вместе с `received_at` и временем фактической записи

---

## 8. Тестирование

Используются:

- xUnit;
- FluentAssertions;
- NSubstitute

Тестами покрыты как базовые сценарии, так и сбои/конкурентность

### Нормализация

Проверяются parsers Alpha, Beta и Gamma и преобразование разных внешних форматов в одну внутреннюю модель

### Дедупликация

Проверяются:

- повторная запись одного тика;
- истечение deduplication window;
- конкурентный вызов дедупликатора из большого количества параллельных операций;
- независимость TTL от wall-clock времени

### Backpressure

Проверяется, что bounded channel действительно блокирует producer при полном буфере, а не растёт неограниченно

### Reconnect и сбои источников

Проверяются:

- многократный reconnect после нескольких обрывов;
- продолжение работы исправного источника при сбоях другого;
- reconnect после idle-timeout;
- отбрасывание replay-дубликата после переподключения

### Ошибки БД

Проверяются:

- retry транзиентной ошибки;
- отсутствие бессмысленных retry для постоянной ошибки;
- fallback в dead-letter;
- явный dropped-counter, если недоступны и БД, и dead-letter;
- корректная обработка отмены во время записи batch

### Graceful shutdown

Проверяются:

- flush последнего неполного batch;
- drain уже принятых тиков;
- повторный `StopAsync`;
- корректный учёт тиков при forced shutdown без двойного счёта

---

## 9. Известные ограничения и trade-offs

### 9.1. Нет автоматического replay dead-letter

Неуспешные batches сохраняются на диск, но recovery worker, автоматически возвращающий их в PostgreSQL после восстановления БД, не реализован

Для тестового задания выбран прозрачный и проверяемый fallback. В production это потребовало бы отдельного recovery-процесса и идемпотентной повторной доставки

### 9.2. Deduplication state хранится в памяти

После рестарта агрегатора двухминутное in-memory окно теряется

Повторные exact duplicates всё равно не размножаются в PostgreSQL благодаря детерминированному `id` и `ON CONFLICT DO NOTHING`

Для нескольких реплик потребовалось бы внешнее shared-state решение либо дедупликация по биржевому `tradeId`/`sequenceNumber`

### 9.3. Контентный ключ дедупликации является компромиссом

Сейчас ключ:

```text
source + symbol + price + volume + timestamp
```

Если реальная биржа передаст две разные сделки с полностью одинаковым набором этих полей, они будут считаться дублем

В production предпочтительным ключом был бы стабильный exchange `trade id` или `sequence number`, если он доступен. Контентный ключ можно оставить fallback-стратегией

### 9.4. Dead-letter хранится на локальном диске

Это достаточно для одного локального контейнера тестового стенда, но не для горизонтально масштабируемой production-системы

В production dead-letter должен находиться во внешнем durable storage: объектном хранилище, отдельной БД или брокере сообщений

### 9.5. Нет message broker

Внутри одного процесса bounded `Channel` проще и позволяет явно показать backpressure и lifecycle

При нескольких репликах агрегатора или необходимости durable buffering логичным продолжением был бы Kafka/Redpanda или другой внешний stream/broker

### 9.6. Нет TLS и аутентификации WebSocket

Стенд локальный и предназначен для проверки pipeline

Production endpoints должны использовать `wss://`, authentication/authorization и secret management

### 9.7. Метрики in-memory

В решении есть `/metrics` и периодический structured log, но нет Prometheus/OpenTelemetry exporter

### 9.8. Схема БД создаётся init.sql

EF migrations не используются. Для небольшого тестового стенда это минимальный и воспроизводимый вариант

В production схема должна версионироваться полноценным механизмом миграций

### 9.9. Poison batch не делится автоматически

Если постоянная ошибка вызвана одним некорректным тиком внутри batch, весь batch отправляется в dead-letter

Более развитая стратегия могла бы бинарно делить batch для локализации проблемной записи

### 9.10. `/health` не проверяет PostgreSQL

Endpoint отражает состояние WebSocket-источников и pipeline, но не выполняет отдельную readiness-проверку БД

В production readiness probe должен учитывать доступность persistence

### 9.11. Forced shutdown и dead-letter

При отмене уже переданный writer-у batch сначала пытается сохраниться в dead-letter, поэтому fallback-запись сознательно не отменяется основным cancellation token

Это уменьшает риск потери уже принятых данных, но патологически зависшая файловая система теоретически может увеличить время завершения процесса

---

## 10. Основные конфигурационные параметры

Настройки находятся в:

```text
src/BrokerPilot.MarketData.Aggregator/appsettings.json
```

Ключевые значения по умолчанию:

| Параметр | Значение |
|---|---:|
| `ChannelCapacity` | 20 000 |
| `BatchSize` | 500 |
| `FlushInterval` | 250 ms |
| `DeduplicationWindow` | 2 min |
| `DatabaseMaxRetries` | 4 |
| `ReconnectInitialDelay` | 250 ms |
| `ReconnectMaxDelay` | 10 s |
| `ReconnectStabilityThreshold` | 30 s |
| `IdleTimeout` | 5 s |
| `DrainTimeout` | 15 s |
| `MetricsLogInterval` | 10 s |
