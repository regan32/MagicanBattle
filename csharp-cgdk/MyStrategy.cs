using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Com.CodeGame.CodeWizards2016.DevKit.CSharpCgdk.Model;

namespace Com.CodeGame.CodeWizards2016.DevKit.CSharpCgdk {

    public sealed class MyStrategy : IStrategy
    {
        [Flags]
        private enum State
        {
            Moving = 1,
            Stuck = 2,
            EnemiesInSight = 4,
            Attacked = 8,
            Retreed = 16,
            MovingToBonus = 32,
            WaitingForCreeps = 64,
        }

        private bool initialized = false;
        private static double WAYPOINT_RADIUS = 100.0D;
        private readonly Dictionary<LaneType, Point2D[]> waypointsByLane = new Dictionary<LaneType, Point2D[]>();
        private Random random;

        private Queue<Wizard> lastWizardStates = new Queue<Wizard>();

        private List<LivingUnit> enemiesInRange;
        private List<LivingUnit> blockers;
        private List<Point2D> globalWaypoints;
        private double[,] roadMap;

        public void Move(Wizard self, World world, Game game, Move move)
        {
            InitializeConstants(self, game);
            InitializeUnits(self, world, game);
            var state = GetMagicanState(self, world, game);
            Console.WriteLine(state);

            if (state.HasFlag(State.Retreed) || state.HasFlag(State.WaitingForCreeps))
            {
                Console.WriteLine("RETREED!!!");
                GoForward(self, game, move, GetPreviousWaypoint(self));
                state &= State.Stuck;
            }

            if (state.HasFlag(State.Attacked))
            {
                Console.WriteLine("Attacked!");
                //GoBackward(self, game, move, GetPreviousWaypoint(self));
                //state &= State.Stuck;
            }

            if (state.HasFlag(State.Moving))
            {
                Console.WriteLine("Moving to target");
                GoForward(self, game, move, GetNextwayPointInGraph(self,GetNextWaypoint(self)));
                state &= State.EnemiesInSight;
            }
            if (state.HasFlag(State.EnemiesInSight) && AttackEnemy(self, game, move))
            {
                state = 0;
            }

            if (state.HasFlag(State.Stuck))
            {
                Console.WriteLine("Trying Unstack");
                TryUnstuck(move, self, game);
            }

//            ChooseTarget(self, world, game, move);
//            var skills = self.Skills.ToList();

            if (this.lastWizardStates.Count >= 10)
            {
                this.lastWizardStates.Dequeue();
            }
            this.lastWizardStates.Enqueue(self);
        }

        private bool AttackEnemy(Wizard self, Game game, Move move)
        {
            var enemyUnit = GetUnitForAttack(self, game);
            if (enemyUnit != null)
            {
                Console.WriteLine($"Attacking unit distanse = {self.GetDistanceTo(enemyUnit)}, angle = {self.GetAngleTo(enemyUnit)}, x = {enemyUnit.X}, y = {enemyUnit.Y}");
                move.Turn = self.GetAngleTo(enemyUnit);
                move.Speed = enemiesInRange.Min(a => a.GetDistanceTo(self)) <= self.CastRange - self.Radius * 2 ? -game.WizardBackwardSpeed : 0;
                move.CastAngle = self.GetAngleTo(enemyUnit);
                move.Action = GetWeapon();
                return true;
            }
            return false;
        }
        private void GoBackward(Wizard self, Game game, Move move, Point2D point)
        {
            move.Speed = -game.WizardForwardSpeed;
            move.Turn = Math.PI/2 - self.GetAngleTo(point.X, point.Y);
        }
        private void GoForward(Wizard self, Game game, Move move, Point2D point)
        {
            move.Speed = game.WizardForwardSpeed;
            point = GetNextwayPointInGraph(self, point);
            move.Turn = self.GetAngleTo(point.X, point.Y);
        }

        private void TryUnstuck(Move move, Wizard self, Game game)
        {
            var stuckIterations = this.lastWizardStates.Count(a => self.GetDistanceTo(a) < 1);

            if (stuckIterations == 10)
            {
                this.lastWizardStates.Clear();
            }
            else if (stuckIterations == 7)
            {
                move.Speed = -move.Speed;
            }
            else
            {
                move.StrafeSpeed = stuckIterations < 3 ? game.WizardStrafeSpeed : -game.WizardStrafeSpeed;
            }
        }
        private State GetMagicanState(Wizard self, World world, Game game)
        {
            State state = 0;
            if(this.enemiesInRange.Any())
                state |= State.EnemiesInSight;

            if(this.lastWizardStates.Any(a => a.Life > self.Life))
                state |= State.Attacked;

            if ((double) self.MaxLife/self.Life < 0.5 && state.HasFlag(State.Attacked))
                state |= State.Retreed;

            if (world.TickCount > 0 && world.TickCount < game.TickCount - game.BonusAppearanceIntervalTicks)
            {
                if(world.TickCount == game.BonusAppearanceIntervalTicks)
                    state |= State.MovingToBonus;
            }

            if (lastWizardStates.Count > 0 && this.lastWizardStates.Peek().GetDistanceTo(self) < 1)
            {
                 state |= State.Stuck;
            }
            else
            {
                 state |= State.Moving;
            }

            if (world.Minions.Where(a => a.GetDistanceTo(self) <= game.WizardVisionRange * 2 && a.Faction == self.Faction).All(a => a.X * (game.MapSize - a.Y) / 2 <= self.X * (game.MapSize - self.Y) / 2))
            {
                state |= State.WaitingForCreeps;
            }
            return state;
        }

        private void InitializeUnits(Wizard self, World world, Game game)
        {
            Func<LivingUnit, bool> enemiesSelector = unit => unit.Faction != self.Faction && unit.Faction != Faction.Neutral && self.GetDistanceTo(unit) <= self.CastRange - (self.Radius + unit.Radius);
            Func<LivingUnit, bool> aliesSelector = unit => unit.Faction == self.Faction && self.GetDistanceTo(unit) <= self.CastRange - (self.Radius + unit.Radius);

            var enemies = new List<LivingUnit>();
            enemies.AddRange(world.Wizards.Where(enemiesSelector));
            enemies.AddRange(world.Buildings.Where(enemiesSelector));
            enemies.AddRange(world.Minions.Where(enemiesSelector));

            var aliases = new List<LivingUnit>();
            foreach (var unit in world.Wizards)
            {
                if(unit.IsMe)
                    continue;

                if(enemiesSelector(unit))
                    enemies.Add(unit);

                if (aliesSelector(unit))
                    aliases.Add(unit);
            }
            //TODO REMOVE DUBLICATE
            foreach (var unit in world.Minions)
            {
                if (enemiesSelector(unit))
                    enemies.Add(unit);

                if (aliesSelector(unit))
                    aliases.Add(unit);
            }

            foreach (var unit in world.Buildings)
            {
                if (enemiesSelector(unit))
                    enemies.Add(unit);

                if (aliesSelector(unit))
                    aliases.Add(unit);
            }

            enemiesInRange = enemies;
            blockers = aliases;
        }

        private void ChooseTarget(Wizard self, World world, Game game, Move move)
        {
            if (this.blockers.Count != 0 && this.blockers.All(a => a.X*(game.MapSize - a.Y)/2 <= self.X*(game.MapSize - self.Y)/2))
            {
                var point = GetPreviousWaypoint(self);
                move.Turn = self.GetAngleTo(point.X, point.Y);
                move.Speed = game.WizardBackwardSpeed;
                return;
            }
            if (!enemiesInRange.Any())
            {
                if (this.lastWizardStates.Count > 0)
                {
                    var lastState = this.lastWizardStates.Peek();
                    if (lastState.GetDistanceTo(self) < 1)
                    {
                        var stuckIterations = this.lastWizardStates.Count(a => self.GetDistanceTo(a) < 1);

                        if (stuckIterations == 10)
                        {
                            this.lastWizardStates.Clear();
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

                var point = GetNextwayPointInGraph(self, GetNextWaypoint(self));
                move.Turn = self.GetAngleTo(point.X, point.Y);
                return;
            }
            else
            {
//                if (this.lastWizardStates.Any(a => a.Life > self.Life))
//                {
//                    Console.WriteLine("FALLBACK!");
//                    var point = GetPreviousWaypoint(self);
//                    move.Turn = self.GetAngleTo(point.X, point.Y);
//                    move.Speed = game.WizardBackwardSpeed;
//                    return;
//                }

                var enemyUnit = GetUnitForAttack(self, game);
                if (enemyUnit != null)
                {
                    Console.WriteLine($"Attacking unit distanse = {self.GetDistanceTo(enemyUnit)}, angle = {self.GetAngleTo(enemyUnit)}, x = {enemyUnit.X}, y = {enemyUnit.Y}");
                    move.Turn = self.GetAngleTo(enemyUnit);
                    move.Speed = enemiesInRange.Min(a => a.GetDistanceTo(self)) <= self.CastRange /2 ? -game.WizardBackwardSpeed : 0;
                    move.CastAngle = move.Turn;
                    move.Action = GetWeapon();
                }
                else
                {
                    //enemyUnit = enemiesInRange.FirstOrDefault();
                    //move.CastAngle = self.GetAngleTo(enemyUnit);
                    //move.Action = GetWeapon();
                    var point = GetNextwayPointInGraph(self, GetNextWaypoint(self));
                    move.Turn = self.GetAngleTo(point.X, point.Y);
                    move.Speed = game.WizardBackwardSpeed;
                }
            }
        }

        private LivingUnit GetUnitForAttack(Wizard self, Game game)
        {
            LivingUnit nearest = null;
            foreach (var enemy in this.enemiesInRange)
            {
                //int angle = (int)(self.GetAngleTo(livingUnit) * 180 / Math.PI);
                var distC = self.GetDistanceTo(enemy);
                bool blockerExists =  blockers.Any(a =>
                {
                    var distB = self.GetDistanceTo(a);
                    var distA = enemy.GetDistanceTo(a);

                    var p = 0.5*(distA + distB + distC);
                    var height = 2*Math.Sqrt(p*(p - distB)*(p - distC)*(p - distA))/distA;

                    Console.WriteLine(height);

                    return height <= a.Radius + game.MagicMissileRadius;
                    
                });

                if (!blockerExists)
                {
                    nearest = enemy;
                    break;
                }
            }
            return nearest;
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

            this.globalWaypoints = new List<Point2D>()
            {
                new Point2D(200, 3800), // 0 to 1, 0 to 5
                new Point2D(200, 3200), // 1 to 0, 1 to 2, 1 to 6
                new Point2D(200, 2000), // 2 to 1, 2 to 3
                new Point2D(200, 600), // 3 to 2, 
                new Point2D(200, 200), // 4
                new Point2D(600, 3800),// 5
                new Point2D(600, 3200), // 6
                new Point2D(600, 600),
                new Point2D(600, 200),
                new Point2D(1200, 2800),
                new Point2D(1200, 1200),
                new Point2D(1800, 2200),
                new Point2D(1800, 1800),
                new Point2D(2000, 3800),
                new Point2D(2000, 2000),
                new Point2D(2000, 200),
                new Point2D(2200, 2200),
                new Point2D(2200, 1800),
                new Point2D(2800, 2800),
                new Point2D(2800, 1200),
                new Point2D(3200, 3800),
                new Point2D(3200, 3200),
                new Point2D(3200, 600),
                new Point2D(3200, 200),
                new Point2D(3800, 3800),
                new Point2D(3800, 3200),
                new Point2D(3800, 2000),
                new Point2D(3800, 600),
                new Point2D(3800, 200),
            };

            this.roadMap = new double[this.globalWaypoints.Count, this.globalWaypoints.Count];
            this.roadMap[0, 1] = this.roadMap[1, 0] = this.globalWaypoints[0].GetDistanceTo(this.globalWaypoints[1]);
            this.roadMap[0, 5] = this.roadMap[5, 0] = this.globalWaypoints[0].GetDistanceTo(this.globalWaypoints[6]);
            //1
            this.roadMap[1, 2] = this.roadMap[2, 1] = this.globalWaypoints[1].GetDistanceTo(this.globalWaypoints[2]);
            this.roadMap[1, 6] = this.roadMap[6, 0] = this.globalWaypoints[1].GetDistanceTo(this.globalWaypoints[6]);
            //2
            this.roadMap[2, 3] = this.roadMap[3, 2] = this.globalWaypoints[2].GetDistanceTo(this.globalWaypoints[3]);
            //3
            this.roadMap[3, 4] = this.roadMap[4, 3] = this.globalWaypoints[3].GetDistanceTo(this.globalWaypoints[3]);
            this.roadMap[3, 7] = this.roadMap[7, 3] = this.globalWaypoints[3].GetDistanceTo(this.globalWaypoints[7]);
            //4
            this.roadMap[4, 8] = this.roadMap[8, 4] = this.globalWaypoints[4].GetDistanceTo(this.globalWaypoints[8]);
            //5
            this.roadMap[5, 6] = this.roadMap[6, 5] = this.globalWaypoints[5].GetDistanceTo(this.globalWaypoints[6]);
            this.roadMap[5, 13] = this.roadMap[13, 5] = this.globalWaypoints[5].GetDistanceTo(this.globalWaypoints[13]);
            //6
            this.roadMap[6, 9] = this.roadMap[9, 6] = this.globalWaypoints[6].GetDistanceTo(this.globalWaypoints[9]);
            //7
            this.roadMap[7, 8] = this.roadMap[8, 7] = this.globalWaypoints[7].GetDistanceTo(this.globalWaypoints[8]);
            this.roadMap[7, 10] = this.roadMap[10, 7] = this.globalWaypoints[7].GetDistanceTo(this.globalWaypoints[10]);
            //8
            this.roadMap[8, 8] = this.roadMap[8, 7] = this.globalWaypoints[7].GetDistanceTo(this.globalWaypoints[8]);
            this.roadMap[7, 10] = this.roadMap[10, 7] = this.globalWaypoints[7].GetDistanceTo(this.globalWaypoints[10]);
            //9
            this.roadMap[9, 11] = this.roadMap[11, 9] = this.globalWaypoints[9].GetDistanceTo(this.globalWaypoints[11]);
            //10
            this.roadMap[10, 12] = this.roadMap[12, 10] = this.globalWaypoints[10].GetDistanceTo(this.globalWaypoints[12]);
            //11
            this.roadMap[11, 12] = this.roadMap[12, 11] = this.globalWaypoints[11].GetDistanceTo(this.globalWaypoints[12]);
            this.roadMap[11, 14] = this.roadMap[14, 11] = this.globalWaypoints[11].GetDistanceTo(this.globalWaypoints[14]);
            this.roadMap[11, 16] = this.roadMap[16, 11] = this.globalWaypoints[11].GetDistanceTo(this.globalWaypoints[16]);
            //12
            this.roadMap[12, 17] = this.roadMap[17, 12] = this.globalWaypoints[12].GetDistanceTo(this.globalWaypoints[17]);
            this.roadMap[12, 14] = this.roadMap[14, 12] = this.globalWaypoints[12].GetDistanceTo(this.globalWaypoints[14]);
            //13 our bottom to corner
            this.roadMap[13, 20] = this.roadMap[20, 13] = this.globalWaypoints[13].GetDistanceTo(this.globalWaypoints[20]);
            //14 - mid 2000/2000
            this.roadMap[14, 16] = this.roadMap[16, 14] = this.globalWaypoints[14].GetDistanceTo(this.globalWaypoints[16]);
            this.roadMap[14, 17] = this.roadMap[17, 14] = this.globalWaypoints[14].GetDistanceTo(this.globalWaypoints[17]);
            //15 - enemy top to enemy base
            this.roadMap[15, 23] = this.roadMap[23, 15] = this.globalWaypoints[15].GetDistanceTo(this.globalWaypoints[23]);
            //16 to bottom bonus
            this.roadMap[16, 18] = this.roadMap[18, 16] = this.globalWaypoints[16].GetDistanceTo(this.globalWaypoints[18]);
            //17 enemy middle 
            this.roadMap[17, 19] = this.roadMap[19, 17] = this.globalWaypoints[17].GetDistanceTo(this.globalWaypoints[19]);
            //18 
            this.roadMap[18, 21] = this.roadMap[21, 18] = this.globalWaypoints[18].GetDistanceTo(this.globalWaypoints[21]);
            //19
            this.roadMap[19, 22] = this.roadMap[22, 19] = this.globalWaypoints[19].GetDistanceTo(this.globalWaypoints[22]);
            //20
            this.roadMap[20, 21] = this.roadMap[21, 20] = this.globalWaypoints[20].GetDistanceTo(this.globalWaypoints[21]);
            this.roadMap[20, 24] = this.roadMap[24, 20] = this.globalWaypoints[20].GetDistanceTo(this.globalWaypoints[24]);
            //21
            this.roadMap[21, 25] = this.roadMap[25, 21] = this.globalWaypoints[21].GetDistanceTo(this.globalWaypoints[25]);
            //24
            this.roadMap[24, 25] = this.roadMap[25, 24] = this.globalWaypoints[24].GetDistanceTo(this.globalWaypoints[25]);
            //25
            this.roadMap[25, 26] = this.roadMap[26, 25] = this.globalWaypoints[25].GetDistanceTo(this.globalWaypoints[26]);
            //26
            this.roadMap[26, 27] = this.roadMap[27, 26] = this.globalWaypoints[26].GetDistanceTo(this.globalWaypoints[27]);

            waypointsByLane.Add(LaneType.Middle, new Point2D[]{
                   // new Point2D(100.0D, mapSize - 100.0D),
                   // new Point2D(600.0D, mapSize - 200.0D),
                    globalWaypoints[6],
                    globalWaypoints[11],
                    globalWaypoints[16],
                    globalWaypoints[17],
                    globalWaypoints[19],
                    globalWaypoints[22],
        });
        }

        private Point2D GetNextwayPointInGraph(Wizard self, Point2D target)
        {
            foreach (var graphWaypoint in this.globalWaypoints.OrderBy(a => a.GetDistanceTo(self)))
            {
                if(graphWaypoint.GetDistanceTo(self) <= WAYPOINT_RADIUS)
                    continue;

                if (target.GetDistanceTo(graphWaypoint) < target.GetDistanceTo(self))
                    return graphWaypoint;
            }

            return target;
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

        private Point2D GetPreviousWaypoint(Wizard self)
        {
            var waypoints = this.waypointsByLane[LaneType.Middle];
            Point2D firstWaypoint = waypoints[0];

            for (int waypointIndex = waypoints.Length - 1; waypointIndex > 0; --waypointIndex)
            {
                Point2D waypoint = waypoints[waypointIndex];

                if (waypoint.GetDistanceTo(self) <= WAYPOINT_RADIUS)
                {
                    return waypoints[waypointIndex - 1];
                }

                if (firstWaypoint.GetDistanceTo(waypoint) < firstWaypoint.GetDistanceTo(self))
                {
                    return waypoint;
                }
            }

            return firstWaypoint;
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