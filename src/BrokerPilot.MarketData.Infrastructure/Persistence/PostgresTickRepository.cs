using System.Text;
using BrokerPilot.MarketData.Application.Abstractions;
using BrokerPilot.MarketData.Domain;
using Npgsql;
using NpgsqlTypes;

namespace BrokerPilot.MarketData.Infrastructure.Persistence;

/// <summary>
/// Пишет батч одним multi-row INSERT с ON CONFLICT DO NOTHING
/// </summary>
public sealed class PostgresTickRepository(NpgsqlDataSource dataSource) : ITickRepository
{
    public async Task<int> WriteBatchAsync(
        IReadOnlyList<NormalizedTick> ticks,
        CancellationToken cancellationToken)
    {
        if (ticks.Count == 0)
        {
            return 0;
        }

        var sql = BuildInsertSql(ticks.Count);
        await using var command = dataSource.CreateCommand(sql);

        for (var index = 0; index < ticks.Count; index++)
        {
            var tick = ticks[index];

            command.Parameters.AddWithValue($"id{index}", NpgsqlDbType.Uuid, tick.Id);
            command.Parameters.AddWithValue($"source{index}", NpgsqlDbType.Varchar, tick.Source);
            command.Parameters.AddWithValue($"symbol{index}", NpgsqlDbType.Varchar, tick.Symbol);
            command.Parameters.AddWithValue($"price{index}", NpgsqlDbType.Numeric, tick.Price);
            command.Parameters.AddWithValue($"volume{index}", NpgsqlDbType.Numeric, tick.Volume);
            command.Parameters.AddWithValue($"eventTime{index}", NpgsqlDbType.TimestampTz, tick.Timestamp);
            command.Parameters.AddWithValue($"receivedAt{index}", NpgsqlDbType.TimestampTz, tick.ReceivedAt);
        }

        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string BuildInsertSql(int count)
    {
        var builder = new StringBuilder(
            "INSERT INTO market_ticks (id, source, symbol, price, volume, event_time, received_at) VALUES ");

        for (var index = 0; index < count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            builder.Append('(')
                .Append("@id").Append(index).Append(',')
                .Append("@source").Append(index).Append(',')
                .Append("@symbol").Append(index).Append(',')
                .Append("@price").Append(index).Append(',')
                .Append("@volume").Append(index).Append(',')
                .Append("@eventTime").Append(index).Append(',')
                .Append("@receivedAt").Append(index)
                .Append(')');
        }

        builder.Append(" ON CONFLICT (id) DO NOTHING;");
        return builder.ToString();
    }
}
