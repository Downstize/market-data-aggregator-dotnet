using System.Data.Common;
using System.Net.Sockets;
using BrokerPilot.MarketData.Application.Abstractions;

namespace BrokerPilot.MarketData.Infrastructure.Persistence;

/// <summary>
/// Обходит цепочку InnerException, потому что Npgsql оборачивает интересующий нас сбой
/// (сброс сокета, таймаут подключения) в общий NpgsqlException
/// </summary>
public sealed class NpgsqlTransientErrorDetector : ITransientDatabaseErrorDetector
{
    public bool IsTransient(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            // DbException.IsTransient реализован в Npgsql: true для отказа соединения, остановки
            // сервера, deadlock и serialization failure; false для нарушения constraint,
            // отсутствующих таблиц и ошибок данных. Свой список SQLSTATE неизбежно устарел бы
            if (current is DbException { IsTransient: true })
            {
                return true;
            }

            if (current is TimeoutException or SocketException or IOException)
            {
                return true;
            }
        }

        return false;
    }
}
