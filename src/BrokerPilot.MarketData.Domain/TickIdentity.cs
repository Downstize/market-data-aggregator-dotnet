using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace BrokerPilot.MarketData.Domain;

/// <summary>
/// Детерминированный идентификатор тика: одинаковое содержимое всегда даёт одинаковый Guid,
/// в любом процессе и после любого рестарта. Это делает Id пригодным сразу для двух ролей -
/// ключа дедупликации в памяти и первичного ключа в БД, обеспечивающего идемпотентность вставки
/// </summary>
public static class TickIdentity
{
    public static Guid Create(
        string source,
        string symbol,
        decimal price,
        decimal volume,
        DateTimeOffset timestamp)
    {
        // \u001F — управляющий символ Unit Separator, он не встречается в данных. Разделитель
        // обязателен: без него пары ("AB", "C") и ("A", "BC") склеились бы в одну строку "ABC"
        // и получили бы один Id
        var canonical = string.Join(
            '\u001F',
            source,
            symbol,
            // G29 отбрасывает незначащие нули. Это принципиально: 1.085m и 1.08500m равны по
            // значению, но ToString() без формата даёт разные строки, потому что decimal хранит
            // scale. Одна биржа пришлёт "1.085", другая 1.08500 - без нормализации представления
            // один и тот же тик получил бы разные Id, и дедупликация ломалась бы молча.
            // InvariantCulture - потому что в русской или немецкой локали разделителем стала бы
            // запятая, и все Id изменились бы при запуске на другой машине
            price.ToString("G29", CultureInfo.InvariantCulture),
            volume.ToString("G29", CultureInfo.InvariantCulture),
            // Ticks - единственное представление времени без вариантов форматирования и без
            // потери точности (100 нс)
            timestamp.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture));

        var bytes = Encoding.UTF8.GetBytes(canonical);
        var hash = SHA256.HashData(bytes);

        // Берём первые 128 бит из 256: Guid - ровно 16 байт и ложится в uuid PostgreSQL.
        // По парадоксу дней рождения коллизия становится вероятной около 2^64 элементов,
        // то есть при 1000 тиков/сек - примерно через 580 миллионов лет
        return new Guid(hash.AsSpan(0, 16));
    }
}
