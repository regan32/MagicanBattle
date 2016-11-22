using System;
using System.Collections.Generic;
using System.Linq;
using Com.CodeGame.CodeWizards2016.DevKit.CSharpCgdk.Model;

namespace Com.CodeGame.CodeWizards2016.DevKit.CSharpCgdk {

    public sealed class MyStrategy : IStrategy
    {
        private bool initialized = false;
        private static double WAYPOINT_RADIUS = 100.0D;
        private readonly Dictionary<LaneType, Point2D[]> waypointsByLane = new Dictionary<LaneType, Point2D[]>();

        private Random random;

        private Queue<Wizard> lastWizardState = new Queue<Wizard>(); 
        public void Move(Wizard self, World world, Game game, Move move)
        {
            InitializeConstants(self, game);

            ChooseTarget(self, world, game, move);
            var skills = self.Skills.ToList();

            if (this.lastWizardState.Count >= 10)
            {
                lastWizardState.Dequeue();
            }
            lastWizardState.Enqueue(self);
        }

        private void ChooseTarget(Wizard self, World world, Game game, Move move)
        {
            Func<LivingUnit, bool> selector = unit => unit.Faction != self.Faction && unit.Faction != Faction.Neutral && self.GetDistanceTo(unit) <= game.WizardCastRange;

            var enemies = GetUnitsInRange(self, world, game);
            //if (world.Wizards.Any(selector) || world.Minions.Any(selector) || world.Minions.Any(selector))
            if(!enemies.Any())
            {
                if (this.lastWizardState.Count > 0)
                {
                    var lastState = lastWizardState.Peek();
                    if (lastState.GetDistanceTo(self) < 1)
                    {
                        var stuckIterations = this.lastWizardState.Count(a => self.GetDistanceTo(a) < 1);

                        if (stuckIterations == 10)
                        {
                            lastWizardState.Clear();
                        }
                        else if (stuckIterations == 7)
                        {
                            move.Speed = -game.WizardForwardSpeed;
                        }
                        else
                        {
                            move.StrafeSpeed = stuckIterations < 3 ? game.WizardStrafeSpeed : -game.WizardStrafeSpeed;
                        }

                        return;
                    }
                }

                move.Speed = game.WizardForwardSpeed;

                var point = GetNextWaypoint(self);
                move.Turn = self.GetAngleTo(point.X, point.Y);
            }
            else
            {
                AttackStrategy(enemies, self, world, game, move);
            }
        }

        private IDictionary<int, int> GetFriendlyUnitsLocation(World world, Wizard self, Game game)
        {
            Func<LivingUnit, bool> selector = unit => unit.Faction == self.Faction && self.GetDistanceTo(unit) <= game.WizardCastRange;
            var collection = new Dictionary<int, int>();
            foreach (var friend in world.Wizards.Where(selector))
            {
                int angle = (int)(self.GetAngleTo(friend) * 180 / Math.PI);
                if (!collection.ContainsKey(angle))
                collection.Add(angle, (int)self.GetDistanceTo(friend));
            }

            foreach (var friend in world.Minions.Where(selector))
            {
                int angle = (int)(self.GetAngleTo(friend) * 180 / Math.PI);
                if (!collection.ContainsKey(angle))
                    collection.Add(angle, (int)self.GetDistanceTo(friend));
            }

            foreach (var friend in world.Buildings.Where(selector))
            {
                int angle = (int)(self.GetAngleTo(friend) * 180 / Math.PI);
                if (!collection.ContainsKey(angle))
                    collection.Add(angle, (int)self.GetDistanceTo(friend));
            }

            return collection;
        } 
        private void AttackStrategy(IReadOnlyCollection<LivingUnit> enemies, Wizard self, World world, Game game, Move move)
        {
            var blockers = GetFriendlyUnitsLocation(world, self, game);

            LivingUnit nearest = null;
            foreach (var livingUnit in enemies)
            {
                int angle = (int)(self.GetAngleTo(livingUnit) * 180 / Math.PI);
                int enemyDistanse = (int)self.GetDistanceTo(livingUnit);

                int friendDistanse;
                if (!blockers.TryGetValue(angle, out friendDistanse) || friendDistanse > enemyDistanse)
                {
                    nearest = livingUnit;
                    break;
                }
            }

            if (nearest != null)
            {
                move.Turn = self.GetAngleTo(nearest);
                //move.StrafeSpeed = direction ? -game.WizardStrafeSpeed : game.WizardStrafeSpeed;
                move.Speed = (self.GetDistanceTo(nearest) < self.CastRange - (self.Radius + nearest.Radius) * 2) ? 0 : -game.WizardBackwardSpeed;
            }
            else
            {
                nearest = enemies.FirstOrDefault();
                int angle = (int)(self.GetAngleTo(nearest) * 180 / Math.PI);
                bool direction = true;
                for (int i = 1; i < 180; i++)
                {
                    if (!blockers.ContainsKey(angle + i))
                    {
                        direction = true;
                        break;
                    }

                    if (!blockers.ContainsKey(angle - i))
                    {
                        direction = false;
                        break;
                    }
                }

                move.StrafeSpeed = direction ? game.WizardStrafeSpeed : -game.WizardStrafeSpeed;
                move.Turn = self.GetAngleTo(nearest);
            }

            move.CastAngle = move.Turn;
            move.Action = GetWeapon();
        }

        private ActionType? GetWeapon()
        {
            return ActionType.MagicMissile;
        }

        private void InitializeConstants(Wizard self, Game game)
        {
            if(initialized) return;

            initialized = true;

            random = new Random(unchecked((int)game.RandomSeed));

            double mapSize = game.MapSize;

            //MID OR FEED
//            attackStack = new Stack<Point2D>();
//            this.attackStack.Push(new Point2D(mapSize - 100.0D, 100.0D));
//            this.attackStack.Push(new Point2D(mapSize - 200.0D, 600.0D));
//            this.attackStack.Push(new Point2D(mapSize - 800.0D, 800.0D));
//            this.attackStack.Push(new Point2D(800.0D, mapSize - 800.0D));
//            this.attackStack.Push(new Point2D(600.0D, mapSize - 200.0D));
//            this.attackStack.Push(new Point2D(100.0D, mapSize - 100.0D));

            waypointsByLane.Add(LaneType.Middle, new Point2D[]{
                    new Point2D(100.0D, mapSize - 100.0D),
                    new Point2D(600.0D, mapSize - 200.0D),
                    new Point2D(800.0D, mapSize - 800.0D),
                    new Point2D(mapSize - 800.0D, 800.0D),
                    new Point2D(mapSize - 200.0D, 600.0D),
                    new Point2D(mapSize - 100.0D, 100.0D)
        });

            waypointsByLane.Add(LaneType.Top, new Point2D[]{
                    new Point2D(100.0D, mapSize - 100.0D),
                    new Point2D(100.0D, mapSize - 400.0D),
                    new Point2D(200.0D, mapSize - 800.0D),
                    new Point2D(200.0D, mapSize * 0.75D),
                    new Point2D(200.0D, mapSize * 0.5D),
                    new Point2D(200.0D, mapSize * 0.25D),
                    new Point2D(200.0D, 200.0D),
                    new Point2D(mapSize * 0.25D, 200.0D),
                    new Point2D(mapSize * 0.5D, 200.0D),
                    new Point2D(mapSize * 0.75D, 200.0D),
                    new Point2D(mapSize - 200.0D, 200.0D)
            });

            waypointsByLane.Add(LaneType.Bottom, new Point2D[]{
                    new Point2D(100.0D, mapSize - 100.0D),
                    new Point2D(400.0D, mapSize - 100.0D),
                    new Point2D(800.0D, mapSize - 200.0D),
                    new Point2D(mapSize * 0.25D, mapSize - 200.0D),
                    new Point2D(mapSize * 0.5D, mapSize - 200.0D),
                    new Point2D(mapSize * 0.75D, mapSize - 200.0D),
                    new Point2D(mapSize - 200.0D, mapSize - 200.0D),
                    new Point2D(mapSize - 200.0D, mapSize * 0.75D),
                    new Point2D(mapSize - 200.0D, mapSize * 0.5D),
                    new Point2D(mapSize - 200.0D, mapSize * 0.25D),
                    new Point2D(mapSize - 200.0D, 200.0D)
            });
        }

        private Point2D GetNextWaypoint(Wizard self)
        {
            var waypoints = this.waypointsByLane[LaneType.Middle];
            int lastWaypointIndex = waypoints.Length - 1;
            Point2D lastWaypoint = waypoints[lastWaypointIndex];

            for (int waypointIndex = 0; waypointIndex < lastWaypointIndex; ++waypointIndex)
            {
                Point2D waypoint = waypoints[waypointIndex];

                if (waypoint.GetDistanceTo(self) <= WAYPOINT_RADIUS)
                {
                    return waypoints[waypointIndex + 1];
                }

                if (lastWaypoint.GetDistanceTo(waypoint) < lastWaypoint.GetDistanceTo(self))
                {
                    return waypoint;
                }
            }

            return lastWaypoint;
        }

        private IReadOnlyCollection<LivingUnit> GetUnitsInRange(Wizard self, World world, Game game)
        {
            Func < LivingUnit, bool > selector = unit => unit.Faction != self.Faction && unit.Faction != Faction.Neutral && self.GetDistanceTo(unit) <= game.WizardCastRange;

            var units = new List<LivingUnit>();
            units.AddRange(world.Wizards.Where(selector).OrderBy(a => a.Life));
            units.AddRange(world.Buildings.Where(selector).OrderBy(a => a.Life));
            units.AddRange(world.Minions.Where(selector).OrderBy(a => a.Life));

            return units;
        }

        private class Point2D
        {
            public Point2D(double x, double y)
            {
                X = x;
                Y = y;
            }

            public double X { get; }
            public double Y { get; }

            public double GetDistanceTo(double x, double y)
            {
                double xRange = x - X;
                double yRange = y - Y;
                return Math.Sqrt(xRange * xRange + yRange * yRange); ;
            }

            public double GetDistanceTo(Point2D point)
            {
                return GetDistanceTo(point.X, point.Y);
            }

            public double GetDistanceTo(Unit unit)
            {
                return unit.GetDistanceTo(X, Y);
            }
        }
    }
}