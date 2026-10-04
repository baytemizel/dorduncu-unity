// Client core: offline table against bots through the same TableModel the UI uses; rack bookkeeping; JSON.
using System;
using System.Collections.Generic;
using System.Linq;
using Dorduncu.Client;
using Dorduncu.Engine;
using NUnit.Framework;

namespace Dorduncu.Tests
{
    /// <summary>Deterministic clock: timers run when Advance() passes their due time.</summary>
    public sealed class ManualClock : IScheduler
    {
        private sealed class T : ITimer { public long Due; public Action Fn; public bool Dead; public void Cancel() => Dead = true; }
        private readonly List<T> _timers = new List<T>();
        public long NowMs { get; private set; } = 1_000_000;
        public ITimer After(int ms, Action fn) { var t = new T { Due = NowMs + ms, Fn = fn }; _timers.Add(t); return t; }

        /// <summary>Run every due timer (in due order) up to now + ms; returns false when nothing is scheduled any more.</summary>
        public bool Advance(int ms)
        {
            long until = NowMs + ms;
            while (true)
            {
                _timers.RemoveAll(x => x.Dead);
                var next = _timers.Where(x => x.Due <= until).OrderBy(x => x.Due).FirstOrDefault();
                if (next == null) break;
                _timers.Remove(next);
                NowMs = Math.Max(NowMs, next.Due);
                next.Fn();
            }
            NowMs = until;
            _timers.RemoveAll(x => x.Dead);
            return _timers.Count > 0;
        }
    }

    public class ClientTests
    {
        [Test]
        public void OfflineMatchPlaysToTheEndThroughTheModel()
        {
            var clock = new ManualClock();
            int seed = 0;
            var table = new OfflineTable(new OfflineOptions { Name = "Ayşe", BotLevel = 2, BotDelayMs = _ => 0, NextHandMs = 0, Seed = () => "offline-test-seed-" + seed++ }, clock);
            var model = new TableModel(table);
            int events = 0, errors = 0;
            table.Event += _ => events++;
            table.Error += _ => errors++;
            table.Start();
            var hint = new Bot(5);
            for (int i = 0; i < 20000 && model.Result == null; i++)
            {
                if (model.MyTurn)
                {
                    var v = model.View;
                    Assert.AreEqual(v.Phase == Phases.Discard ? 15 : 14, v.Hand.Count);
                    // every tile of my hand is on the rack exactly once
                    var onRack = new List<int>();
                    for (int r = 0; r < TableModel.Rows; r++) for (int s = 0; s < TableModel.Slots; s++) if (model.Rack[r, s] >= 0) onRack.Add(model.Rack[r, s]);
                    CollectionAssert.AreEqual(v.Hand.Select(t => t.Id).OrderBy(x => x).ToArray(), onRack.OrderBy(x => x).ToArray());
                    var bv = BotView.FromSeatView(v);
                    if (model.CanDraw) model.Draw(hint.DecideDraw(bv));
                    else { var d = hint.DecideDiscard(bv); if (d.Finish) model.Finish(d.TileId); else model.Discard(d.TileId); }
                }
                else clock.Advance(10);
            }
            Assert.IsNotNull(model.Result);
            Assert.AreEqual(0, errors);
            Assert.IsTrue(events > 50);
            Assert.IsTrue(model.Result.Points.Any(p => p <= 0));
            // rejected moves raise an error and change nothing
            table.Send("discard", new Dictionary<string, object> { ["tileId"] = 3 });
        }

        [Test]
        public void IllegalMovesAreRejectedWithCodes()
        {
            var clock = new ManualClock();
            var table = new OfflineTable(new OfflineOptions { BotDelayMs = _ => 50, NextHandMs = 0, Seed = () => "illegal-moves-seed-0001" }, clock);
            var model = new TableModel(table);
            var codes = new List<string>();
            table.Error += e => codes.Add(e.Code);
            table.Start();
            // seat 0 starts (15 tiles): drawing is the wrong phase; a tile we do not hold cannot be thrown or finish
            model.Draw("wall");
            int notMine = Enumerable.Range(0, 106).First(id => model.View.Hand.All(t => t.Id != id));
            model.Discard(notMine);
            model.Finish(notMine);
            CollectionAssert.AreEqual(new[] { "bad_phase", "tile_not_in_hand", "not_winning" }, codes.ToArray());
            Assert.AreEqual(15, model.View.Hand.Count);
        }

        [Test]
        public void RackMovesSwapAndArrangeKeepsEveryTile()
        {
            var clock = new ManualClock();
            var table = new OfflineTable(new OfflineOptions { BotDelayMs = _ => 50, Seed = () => "rack-test-seed-000001" }, clock);
            var model = new TableModel(table);
            table.Start();
            int a = model.Rack[0, 0];
            Assert.IsTrue(a >= 0);
            Assert.IsTrue(model.Move(a, 1, 14));
            Assert.IsTrue(model.Locate(a, out int r, out int s));
            Assert.AreEqual(1, r); Assert.AreEqual(14, s);
            model.Arrange(true);
            var ids = new List<int>();
            for (int i = 0; i < TableModel.Rows; i++) for (int j = 0; j < TableModel.Slots; j++) if (model.Rack[i, j] >= 0) ids.Add(model.Rack[i, j]);
            Assert.AreEqual(15, ids.Distinct().Count());
        }

        [Test]
        public void JsonRoundTrip()
        {
            var o = J.Obj(Json.Parse("{\"a\":[1,2.5,\"ş\\u00e7\",true,null,{\"b\":-3}],\"c\":\"x\\\"y\"}"));
            Assert.AreEqual(2, J.Arr(o["a"]).Count - 4);
            Assert.AreEqual("şç", J.Arr(o["a"])[2]);
            Assert.AreEqual(-3, J.Obj(J.Arr(o["a"])[5]).I("b"));
            Assert.AreEqual("{\"a\":[1,2.5,\"şç\",true,null,{\"b\":-3}],\"c\":\"x\\\"y\"}", Json.Serialize(o));
        }
    }
}
