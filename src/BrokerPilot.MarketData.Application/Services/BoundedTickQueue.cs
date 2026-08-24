using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using BrokerPilot.MarketData.Application.Abstractions;
using BrokerPilot.MarketData.Application.Configuration;
using BrokerPilot.MarketData.Domain;

namespace BrokerPilot.MarketData.Application.Services;

public sealed class BoundedTickQueue : ITickQueue
{
    private readonly Channel<NormalizedTick> _channel;

    public BoundedTickQueue(MarketDataOptions options)
    {
        Capacity = options.ChannelCapacity;
        _channel = Channel.CreateBounded<NormalizedTick>(new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            // Читатель ровно один - TickBatchConsumer. Канал использует это для оптимизации
            SingleReader = true,
            // Писателей несколько - по одному на каждый источник
            SingleWriter = false,
            // Запрещаем выполнять продолжение читателя на потоке писателя. Иначе фид, положивший
            // тик в канал, мог бы прямо там же начать выполнять код батчера, включая ожидание
            // записи в БД, - и на это время перестал бы читать из сокета
            AllowSynchronousContinuations = false
        });
    }

    public int Capacity { get; }

    public int Count => _channel.Reader.CanCount ? _channel.Reader.Count : 0;

    public ValueTask EnqueueAsync(NormalizedTick tick, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(tick, cancellationToken);

    public async ValueTask<NormalizedTick?> ReadAsync(CancellationToken cancellationToken)
    {
        // WaitToReadAsync возвращает false, когда канал завершён И опустошён. Внутренний TryRead
        // нужен потому, что между сигналом и попыткой чтения элемент мог забрать другой читатель:
        // у нас читатель один, но контракт канала этого не гарантирует
        while (await _channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (_channel.Reader.TryRead(out var tick))
            {
                return tick;
            }
        }

        // null - сигнал батчеру «источник закончился». На нём построен корректный drain:
        // после Complete() батчер дочитывает остаток и завершается сам, без отмены
        return null;
    }

    public bool TryRead([MaybeNullWhen(false)] out NormalizedTick tick) => _channel.Reader.TryRead(out tick);

    // TryComplete, а не Complete: последний бросает исключение при повторном вызове, а путь
    // остановки может быть пройден дважды, и падать в этом месте не на чем
    public void Complete() => _channel.Writer.TryComplete();
}
