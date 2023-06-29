
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
    public EventStoreBridge() 
        {
            _client = new EventStoreClient(
                EventStoreClientSettings.Create("esdb://localhost:2113?tls=false")
            );
        }

    public async void Reset (string version, string name) 
        {
            try {
                await _client.DeleteAsync("events" + version + name, StreamState.Any); 
            }
            catch (Exception e) {
                Console.WriteLine(e.Message);
            }
            try {
                await _client.DeleteAsync("snapshots" + version + name, StreamState.Any); 
            }
            catch (Exception e) {
                Console.WriteLine(e.Message);
            }
        }

    public async void AddEvents (string version, List<string> events, string name) 
        {
            var streamName = "events" + version + name;
            var eventData = events.Select(e => new EventData(
                Uuid.NewUuid(),
                "events" + version + name,
                Encoding.UTF8.GetBytes(e)
            ));
            await _client.AppendToStreamAsync(streamName, StreamState.Any, eventData);
        }

    public async void SetSnapshot(int eventId, string version, string snapshot, string name) 
        {
            var streamName = "snapshots" + version + name;
            var eventData = new EventData(
                Uuid.NewUuid(),
                "snapshots" + version + name + " _event_id:" + eventId,
                Encoding.UTF8.GetBytes(snapshot)
            );
            var eventDatas = new List<EventData>() { eventData };
            await _client.AppendToStreamAsync(streamName, StreamState.Any, eventDatas);
        }
    public async Task<List<ResolvedEvent>> ConsumeEvents(string version, string name)
        {
            try {
                await Task.Delay(50);
                Console.WriteLine("ConsumeEvents 1");
                var streamName = "events" + version + name;
                var events =   _client.ReadStreamAsync(Direction.Forwards, streamName, StreamPosition.Start);
                Console.WriteLine("ConsumeEvents 2");
                var eventsRetuned = await  events.ToListAsync();
                Console.WriteLine("ConsumeEvents 3");
                Console.WriteLine("ConsumeEvents exit");
                return eventsRetuned;
            }
            catch (Exception e) {
                return new List<ResolvedEvent>();
            }
        }
    public async Task<Option<(Int64, string)>> ConsumeSnapshots(string version, string name)
        {
            var streamName = "snapshots" + version + name;
            try {
                var snapshots = _client.ReadStreamAsync(Direction.Forwards, streamName, StreamPosition.Start);
                var eventsRetuned = await snapshots.ToListAsync();
                var lastEvent = Encoding.UTF8.GetString(eventsRetuned.Last().Event.Data.ToArray());
                var lastEventId =  eventsRetuned.Last().Event.EventNumber;
                foreach (var e in eventsRetuned) {
                    Console.WriteLine(Encoding.UTF8.GetString(e.Event.Data.ToArray()));
                }
                return Option<(Int64, string)>.Some((lastEventId.ToInt64(), lastEvent));
            }
            catch (Exception e) {
                return Option<(Int64, string)>.None();
            }
        }
    }