CREATE TABLE IF NOT EXISTS market_ticks
(
    id           uuid PRIMARY KEY,
    source       varchar(64)     NOT NULL,
    symbol       varchar(32)     NOT NULL,
    price        numeric(28, 10) NOT NULL CHECK (price > 0),
    volume       numeric(28, 10) NOT NULL CHECK (volume >= 0),
    event_time   timestamptz     NOT NULL,
    received_at  timestamptz     NOT NULL,
    persisted_at timestamptz     NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_market_ticks_source_symbol_event_time
    ON market_ticks (source, symbol, event_time DESC);

CREATE INDEX IF NOT EXISTS ix_market_ticks_event_time
    ON market_ticks (event_time DESC);
