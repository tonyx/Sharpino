namespace Sharpino.Lib.EvStore;
using System;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Client;

public class EventStoreBridge
{
    EventStoreClient _client;
    Dictionary<string, StreamPosition> lastEventIds = new Dictionary<string, StreamPosition>();

    public EventStoreBridge(string connection) 
        {
            _client = new EventStoreClient(
                EventStoreClientSettings.Create(connection)
            );
        }

    public async Task ResetEvents(string version, string name)
        {
            var result1 = _client.ReadStreamAsync(
                Direction.Forwards,
                "events" + version + name, 
                StreamPosition.Start,
                StreamState.Any);

            if (await result1.ReadState == ReadState.StreamNotFound) {
                return;
            }
            else
                try {
                    await _client.DeleteAsync("events" + version + name, StreamState.Any); 
                }
                catch (Exception e) {
                    Console.WriteLine(e.Message);
                }
        }
    public async Task ResetSnapshots(string version, string name)
        {
            var result1 = _client.ReadStreamAsync(
                Direction.Forwards,
                "snapshots" + version + name, 
                StreamPosition.Start,
                StreamState.Any);

            if (await result1.ReadState == ReadState.StreamNotFound) {
                return;
            }
            else
                try {
                    await _client.DeleteAsync("snapshots" + version + name, StreamState.Any); 
                }
                catch (Exception e) {
                    Console.WriteLine(e.Message);
                }
        }


    public async Task AddEvents (string version, List<string> events, string name) 
        {
            var streamName = "events" + version + name;
            var eventData = events.Select(e => new EventData(
                Uuid.NewUuid(),
                "events" + version + name,
                Encoding.UTF8.GetBytes(e)
            ));
            await _client.AppendToStreamAsync(streamName, StreamState.Any, eventData);
        }

    // public async void is suspicious: check the whole snapshots management in eventstore
    public async Task AddSnapshot(UInt64 eventId, string version, string snapshot, string name) 
        {
            var streamName = "snapshots" + version + name;
            var eventData = new EventData(
                Uuid.NewUuid(),
                "snapshots" + version + name,
                Encoding.UTF8.GetBytes(snapshot),
                Encoding.UTF8.GetBytes(eventId.ToString())
            );
            var eventDatas = new List<EventData>() { eventData };
            await _client.AppendToStreamAsync(streamName, StreamState.Any, eventDatas);
        }
    public async Task<List<ResolvedEvent>> ConsumeEvents(string version, string name)
        {
            try {
                var streamName = "events" + version + name;
                var position = lastEventIds.ContainsKey(streamName) ? lastEventIds[streamName] : StreamPosition.Start;

                // read after the stream position of the last snapshot
                var events = _client.ReadStreamAsync(Direction.Forwards, streamName, new StreamPosition(position.ToUInt64() + (UInt64) 1));
                
                // var events = _client.ReadStreamAsync(Direction.Forwards, streamName, position.ToUInt64() + (UInt64) 1);
                var eventsRetuned = await events.ToListAsync();
                foreach (var e in eventsRetuned) {
                    lastEventIds[streamName] = e.OriginalEventNumber;
                }
                return eventsRetuned;
            }
            catch (Exception e) {
                return new List<ResolvedEvent>();
            }
        }

    // see the management of snapshots
    public async Task<Option<(Int64, string)>> ConsumeSnapshots(string version, string name)
        {
            var streamName = "snapshots" + version + name;
            try {
                var snapshots = _client.ReadStreamAsync(Direction.Forwards, streamName, StreamPosition.Start);
                var eventsRetuned = await snapshots.ToListAsync();
                var lastEvent = Encoding.UTF8.GetString(eventsRetuned.Last().Event.Data.ToArray());
                var lastEventId =  eventsRetuned.Last().Event.EventNumber;
                return Option<(Int64, string)>.Some((lastEventId.ToInt64(), lastEvent));
            }
            catch (Exception e) {
                return Option<(Int64, string)>.None();
            }
        }

    public async Task<Option<(UInt64, string)>> GetLastSnapshot(string version, string name)
        {
            var streamName = "snapshots" + version + name;
            try {
                var snapshots = _client.ReadStreamAsync(Direction.Backwards, streamName, StreamPosition.End);
                var snapshot = await snapshots.ElementAtAsync(0);

                // var relatedEventId = Encoding.UTF8.GetString(snapshot.Event.Metadata.ToArray());
                // var eventId = UInt64.Parse(relatedEventId);

                var eventId = UInt64.Parse(Encoding.UTF8.GetString(snapshot.Event.Metadata.ToArray()));

                var snapshotData = Encoding.UTF8.GetString(snapshot.Event.Data.ToArray());
                var lastEventId =  snapshot.Event.EventNumber;

                return Option<(UInt64, string)>.Some((eventId, snapshotData));
            }
            catch (Exception e) {
                return Option<(UInt64, string)>.None();
            }
        }

    }