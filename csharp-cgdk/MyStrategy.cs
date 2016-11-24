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
            Unstucking = 128,
        }

        private bool initialized = false;
        private static double WAYPOINT_RADIUS = 100.0D;
        private readonly Dictionary<LaneType, Point2D[]> waypointsByLane = new Dictionary<LaneType, Point2D[]>();
        private Random random;

        private Point2D globalGoal;
        private Queue<Point2D> pathToGoal;
        private Queue<Wizard> lastWizardStates = new Queue<Wizard>();
        private Queue<Point2D> unstuckQueue = new Queue<Point2D>(); 
        private List<LivingUnit> enemiesInRange;
        private List<LivingUnit> blockers;
        private List<Point2D> globalWaypoints;
        private double[,] roadMap;
        private int lastSavedTick = 0;
        private int localMapSize;
        private double localMapWizardPosition;

        public void Move(Wizard self, World world, Game game, Move move)
        {
            InitializeConstants(self, game);
            InitializeUnits(self, world, game);
            var state = GetMagicanState(self, world, game);
            Console.WriteLine(state);

            if ((state & State.Unstucking) != 0)
            {
                GoForward(self, game, move, this.unstuckQueue.Dequeue());
                state &= State.Stuck;
            }
            if (state.HasFlag(State.Retreed) || state.HasFlag(State.WaitingForCreeps))
            {
                Console.WriteLine("RETREED!!!");
                GoForward(self, game, move, GetNextwayPointInGraph(self, GetPreviousWaypoint(self)));
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
                TryUnstuck(move, self, game, world);
            }

//            ChooseTarget(self, world, game, move);
//            var skills = self.Skills.ToList();


            if (this.lastWizardStates.Count >= 100)
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
            var distanseToTarget = point.GetDistanceTo(self);
            move.Speed = distanseToTarget >= game.WizardForwardSpeed ? -game.WizardForwardSpeed : -distanseToTarget;
            move.Turn = Math.PI/2 - self.GetAngleTo(point.X, point.Y);
        }
        private void GoForward(Wizard self, Game game, Move move, Point2D point)
        {
            var distanseToTarget = point.GetDistanceTo(self);
            if (this.unstuckQueue.Any() && self.GetAngleTo(point.X, point.Y) > Math.PI / 2 - self.GetAngleTo(point.X, point.Y))
            {
                GoBackward(self, game, move, point);
            }
            else
            {
                move.Speed = distanseToTarget >= game.WizardForwardSpeed ? game.WizardForwardSpeed : distanseToTarget;
                move.Turn = self.GetAngleTo(point.X, point.Y);
            }
        }

        private void PutUnitsOnMap(LivingUnit unit, Wizard self, bool[,] image)
        {
            int centerX = (int)Math.Round(localMapWizardPosition + unit.X - self.X);
            int centerY = (int)Math.Round(localMapWizardPosition + unit.Y - self.Y);
            int radius = (int)unit.Radius + (int)self.Radius;

            int d = (5 - radius * 4) / 4;
            int x = 0;
            int y = radius;
            
            do
            {
                // ensure index is in range before setting (depends on your image implementation)
                // in this case we check if the pixel location is within the bounds of the image before setting the pixel
                            
                if (centerX + x >= 0 && centerX + x <= localMapSize - 1 && centerY + y >= 0 && centerY + y <= localMapSize - 1)
                    image[centerX + x, centerY + y] = true;
                if (centerX + x >= 0 && centerX + x <= localMapSize - 1 && centerY - y >= 0 && centerY - y <= localMapSize - 1)
                    image[centerX + x, centerY - y] = true;
                if (centerX - x >= 0 && centerX - x <= localMapSize - 1 && centerY + y >= 0 && centerY + y <= localMapSize - 1)
                    image[centerX - x, centerY + y] = true;
                if (centerX - x >= 0 && centerX - x <= localMapSize - 1 && centerY - y >= 0 && centerY - y <= localMapSize - 1)
                    image[centerX - x, centerY - y] = true;
                if (centerX + y >= 0 && centerX + y <= localMapSize - 1 && centerY + x >= 0 && centerY + x <= localMapSize - 1)
                    image[centerX + y, centerY + x] = true;
                if (centerX + y >= 0 && centerX + y <= localMapSize - 1 && centerY - x >= 0 && centerY - x <= localMapSize - 1)
                    image[centerX + y, centerY - x] = true;
                if (centerX - y >= 0 && centerX - y <= localMapSize - 1 && centerY + x >= 0 && centerY + x <= localMapSize - 1)
                    image[centerX - y, centerY + x] = true;
                if (centerX - y >= 0 && centerX - y <= localMapSize - 1 && centerY - x >= 0 && centerY - x <= localMapSize - 1)
                    image[centerX - y, centerY - x] = true;
                if (d < 0)
                {
                    d += 2 * x + 1;
                }
                else
                {
                    d += 2 * (x - y) + 1;
                    y--;
                }
                x++;
            } while (x <= y);

//            int error = -radius;
//            int x = radius;
//            int y = 0;
//
//            while (x >= y)
//            {
//                int lastY = y;
//
//                error += y;
//                ++y;
//                error += y;
//
//                plot4points(image, centerX, centerY, x, lastY);
//
//                if (error >= 0)
//                {
//                    if (x != lastY)
//                        plot4points(image, centerX, centerY, lastY, x);
//
//                    error -= x;
//                    --x;
//                    error -= x;
//                }
//            }
        }

        void plot4points(bool[,] buffer, int cx, int cy, int x, int y)
        {
            horizontalLine(buffer, cx - x, cy + y, cx + x);
            if (x != 0 && y != 0)
                horizontalLine(buffer, cx - x, cy - y, cx + x);
        }
        void setPixel(bool[,] buffer, int x, int y)
        {
            buffer[y, x] = true;
        }

        void horizontalLine(bool[,] buffer, int x0, int y0, int x1)
        {
            for (int x = x0; x <= x1; ++x)
                if(x >=0 && x < this.localMapSize && y0 >= 0 && y0 <= this.localMapSize)
                setPixel(buffer, x, y0);
        }

        private void TryUnstuck(Move move, Wizard self, Game game, World world)
        {
            var localGoal = this.pathToGoal.Peek();
            if(localGoal.GetDistanceTo(self) < 1)
                return; //not stuck, just need reached

            bool[,] matrix = new bool[this.localMapSize, localMapSize];
            foreach (var units in world.Minions.Where(a => a.GetDistanceTo(self) < this.localMapWizardPosition))
            {
                PutUnitsOnMap(units, self, matrix);
            }
            foreach (var units in world.Buildings.Where(a => a.GetDistanceTo(self) < this.localMapWizardPosition))
            {
                PutUnitsOnMap(units, self, matrix);
            }
            foreach (var units in world.Wizards.Where(a => a.GetDistanceTo(self) < this.localMapWizardPosition))
            {
                PutUnitsOnMap(units, self, matrix);
            }
            foreach (var units in world.Trees.Where(a => a.GetDistanceTo(self) < this.localMapWizardPosition))
            {
                PutUnitsOnMap(units, self, matrix);
            }

            var grid = new StaticGrid(this.localMapSize, localMapSize, matrix);
            var unstuckGoal = new GridPos((int)(localMapWizardPosition + localGoal.X - self.X), (int)(localMapWizardPosition + localGoal.X - self.X));
            if (unstuckGoal.x > this.localMapSize - 1) unstuckGoal.x = this.localMapSize - 1;
            if (unstuckGoal.x < 0) unstuckGoal.x = 0;
            if (unstuckGoal.y > this.localMapSize - 1) unstuckGoal.y = this.localMapSize - 1;
            if (unstuckGoal.y < 0) unstuckGoal.y = 0;
            var path = JumpPointFinder.FindPath(new JumpPointParam(grid, new GridPos((int) this.localMapWizardPosition, (int) this.localMapWizardPosition), unstuckGoal));
            this.unstuckQueue = new Queue<Point2D>(path.Skip(1).Select(a => new Point2D(a.x - localMapWizardPosition + self.X, a.y - localMapWizardPosition + self.Y)));

            var nextPoint = unstuckQueue.Count > 0 ? unstuckQueue.Dequeue() : localGoal;

            Console.WriteLine($"Stuck on {self.X}, {self.Y}, going to {nextPoint.X}, {nextPoint.Y}, iterations { path.Count}");

            GoForward(self, game, move, nextPoint);
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
            if (this.unstuckQueue.Any())
            {
                state |= State.Unstucking;
            }
            if (this.globalGoal != null && this.globalGoal.GetDistanceTo(self) > 1 && lastWizardStates.Count > 0 && this.lastWizardStates.Peek().GetDistanceTo(self) < 1)
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
                    //Console.WriteLine($"Attacking unit distanse = {self.GetDistanceTo(enemyUnit)}, angle = {self.GetAngleTo(enemyUnit)}, x = {enemyUnit.X}, y = {enemyUnit.Y}");
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
            localMapSize = (int)game.WizardVisionRange;
            localMapWizardPosition = game.WizardVisionRange / 2;
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
                    globalWaypoints[9],
                    //globalWaypoints[16],
                    //globalWaypoints[17],
                    globalWaypoints[19],
                    globalWaypoints[22],
        });
        }

        private Queue<Point2D> CalculatePathToTarget(Wizard self, Point2D target)
        {
            int closestIndexToSelf = 0;
            int closestIntexToTarget = 0;
            var queue = new Queue<Point2D>();

            for(int i = 1; i < this.globalWaypoints.Count; i++)
            {
                if (globalWaypoints[i].GetDistanceTo(self) < this.globalWaypoints[closestIndexToSelf].GetDistanceTo(self))
                {
                    closestIndexToSelf = i;
                }

                if (this.globalWaypoints[i].GetDistanceTo(target) < this.globalWaypoints[closestIntexToTarget].GetDistanceTo(target))
                {
                    closestIntexToTarget = i;
                }
            }

            var points = new List<Point>();
            for (int i = 0; i < this.globalWaypoints.Count; i++)
            {
                points.Add(new Point(i, false));
            }

            var rebra = new List<Rebro>();
            for (int i = 0; i < this.globalWaypoints.Count; i++)
                for (int j = 0; j < this.globalWaypoints.Count; j++)
                {
                    if (this.roadMap[i, j] != 0)
                        rebra.Add(new Rebro(new Point(i, false, i), new Point(j, false, j), (float)this.roadMap[i, j]));
                }

            var calculator = new DekstraAlgorim(points.ToArray(), rebra.ToArray());
            calculator.AlgoritmRun(new Point(closestIndexToSelf, false));
            var minPath = calculator.MinPath1(new Point(closestIntexToTarget, false));
            queue.Enqueue(globalWaypoints[closestIndexToSelf]);
            minPath.ForEach(a => queue.Enqueue(this.globalWaypoints[a.Name]));
            queue.Enqueue(globalWaypoints[closestIntexToTarget]);

            return queue;
        }

        private Point2D GetNextwayPointInGraph(Wizard self, Point2D target)
        {
            if (target != this.globalGoal)
            {
                this.pathToGoal = CalculatePathToTarget(self, target);
                this.globalGoal = target;
            }

            if (this.pathToGoal.Count > 1 && this.pathToGoal.Peek().GetDistanceTo(self) <= WAYPOINT_RADIUS)
            {
                this.pathToGoal.Dequeue();
            }

            return this.pathToGoal.Peek();
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

    #region Deikstra

    class Rebro
    {
        public Point FirstPoint { get; private set; }
        public Point SecondPoint { get; private set; }
        public float Weight { get; private set; }

        public Rebro(Point first, Point second, float valueOfWeight)
        {
            FirstPoint = first;
            SecondPoint = second;
            Weight = valueOfWeight;
        }

    }
    class Point : IEquatable<Point>
    {
        public float ValueMetka { get; set; }
        public int Name { get; private set; }
        public bool IsChecked { get; set; }
        public Point predPoint { get; set; }
        public object SomeObj { get; set; }
        public Point(int value, bool ischecked)
        {
            ValueMetka = value;
            IsChecked = ischecked;
            predPoint = new Point();
        }
        public Point(int value, bool ischecked, int name)
        {
            ValueMetka = value;
            IsChecked = ischecked;
            Name = name;
            predPoint = new Point();
        }
        public Point()
        {
        }

        public static bool operator ==(Point a, Point b)
        {
            if (ReferenceEquals(a, b))
                return true;

            if(!ReferenceEquals(a, null) && !ReferenceEquals(b, null))
                return a.Equals(b);

            return false;
        }

        public static bool operator !=(Point a, Point b)
        {
            return !(a == b);
        }

        public bool Equals(Point other)
        {
            if (ReferenceEquals(other, null))
                return false;

            return this.Name == other.Name;
        }
    }

    class DekstraAlgorim
    {

        public Point[] points { get; private set; }
        public Rebro[] rebra { get; private set; }
        public Point BeginPoint { get; private set; }

        public DekstraAlgorim(Point[] pointsOfgrath, Rebro[] rebraOfgrath)
        {
            points = pointsOfgrath;
            rebra = rebraOfgrath;
        }

        /// <summary>
        /// Запуск алгоритма расчета
        /// </summary>
        /// <param name="beginp"></param>
        public void AlgoritmRun(Point beginp)
        {
            if (this.points.Count() == 0 || this.rebra.Count() == 0)
            {
                throw new Exception("Массив вершин или ребер не задан!");
            }
            else
            {
                BeginPoint = beginp;
                OneStep(beginp);
                foreach (Point point in points)
                {
                    Point anotherP = GetAnotherUncheckedPoint();
                    if (anotherP != null)
                    {
                        OneStep(anotherP);
                    }
                    else
                    {
                        break;
                    }

                }
            }

        }

        /// <summary>
        /// Метод, делающий один шаг алгоритма. Принимает на вход вершину
        /// </summary>
        /// <param name="beginpoint"></param>
        public void OneStep(Point beginpoint)
        {
            foreach (Point nextp in Pred(beginpoint))
            {
                if (nextp.IsChecked == false) //не отмечена
                {
                    float newmetka = beginpoint.ValueMetka + GetMyRebro(nextp, beginpoint).Weight;
                    if (nextp.ValueMetka > newmetka)
                    {
                        nextp.ValueMetka = newmetka;
                        nextp.predPoint = beginpoint;
                    }
                    else
                    {

                    }
                }
            }
            beginpoint.IsChecked = true; //вычеркиваем
        }

        /// <summary>
        /// Поиск соседей для вершины. Для неориентированного графа ищутся все соседи.
        /// </summary>
        /// <param name="currpoint"></param>
        /// <returns></returns>
        private IEnumerable<Point> Pred(Point currpoint)
        {
            IEnumerable<Point> firstpoints = from ff in rebra where ff.FirstPoint == currpoint select ff.SecondPoint;
            IEnumerable<Point> secondpoints = from sp in rebra where sp.SecondPoint == currpoint select sp.FirstPoint;
            IEnumerable<Point> totalpoints = firstpoints.Concat<Point>(secondpoints);
            return totalpoints;
        }

        /// <summary>
        /// Получаем ребро, соединяющее 2 входные точки
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        private Rebro GetMyRebro(Point a, Point b)
        {
            //ищем ребро по 2 точкам
            IEnumerable<Rebro> myr = from reb in rebra where (reb.FirstPoint == a & reb.SecondPoint == b) || (reb.SecondPoint == a & reb.FirstPoint == b) select reb;
            if (myr.Any())
            {
                return myr.First();
            }
            else
            {
                throw new Exception("Не найдено ребро между соседями!");
            }
        }

        /// <summary>
        /// Получаем очередную неотмеченную вершину, "ближайшую" к заданной.
        /// </summary>
        /// <returns></returns>
        private Point GetAnotherUncheckedPoint()
        {
            IEnumerable<Point> pointsuncheck = from p in points where p.IsChecked == false select p;
            if (pointsuncheck.Count() != 0)
            {
                float minVal = pointsuncheck.First().ValueMetka;
                Point minPoint = pointsuncheck.First();
                foreach (Point p in pointsuncheck)
                {
                    if (p.ValueMetka < minVal)
                    {
                        minVal = p.ValueMetka;
                        minPoint = p;
                    }
                }
                return minPoint;
            }
            else
            {
                return null;
            }
        }

        public List<Point> MinPath1(Point end)
        {
            List<Point> listOfpoints = new List<Point>();
            Point tempp = new Point();
            tempp = end;
            while (tempp != this.BeginPoint)
            {
                listOfpoints.Add(tempp);
                tempp = tempp.predPoint;
            }

            return listOfpoints;
        }
    }

    #endregion Deikstra
        #region PathFinder
        //THANKS TO https://github.com/juhgiyo/EpPathFinding.cs
        public delegate float HeuristicDelegate(int iDx, int iDy);
    public class JumpPointParam
    {

        public JumpPointParam(BaseGrid iGrid, GridPos iStartPos, GridPos iEndPos, bool iAllowEndNodeUnWalkable = true, bool iCrossCorner = true, bool iCrossAdjacentPoint = true, HeuristicMode iMode = HeuristicMode.EUCLIDEAN)
        {
            switch (iMode)
            {
                case HeuristicMode.MANHATTAN:
                    m_heuristic = new HeuristicDelegate(Heuristic.Manhattan);
                    break;
                case HeuristicMode.EUCLIDEAN:
                    m_heuristic = new HeuristicDelegate(Heuristic.Euclidean);
                    break;
                case HeuristicMode.CHEBYSHEV:
                    m_heuristic = new HeuristicDelegate(Heuristic.Chebyshev);
                    break;
                default:
                    m_heuristic = new HeuristicDelegate(Heuristic.Euclidean);
                    break;
            }
            m_allowEndNodeUnWalkable = iAllowEndNodeUnWalkable;
            m_crossAdjacentPoint = iCrossAdjacentPoint;
            m_crossCorner = iCrossCorner;
            openList = new List<Node>();

            m_searchGrid = iGrid;
            m_startNode = m_searchGrid.GetNodeAt(iStartPos.x, iStartPos.y);
            m_endNode = m_searchGrid.GetNodeAt(iEndPos.x, iEndPos.y);
            if (m_startNode == null)
                m_startNode = new Node(iStartPos.x, iStartPos.y, true);
            if (m_endNode == null)
                m_endNode = new Node(iEndPos.x, iEndPos.y, true);
            m_useRecursive = false;
        }

        public JumpPointParam(BaseGrid iGrid, bool iAllowEndNodeUnWalkable = true, bool iCrossCorner = true, bool iCrossAdjacentPoint = true, HeuristicMode iMode = HeuristicMode.EUCLIDEAN)
        {
            switch (iMode)
            {
                case HeuristicMode.MANHATTAN:
                    m_heuristic = new HeuristicDelegate(Heuristic.Manhattan);
                    break;
                case HeuristicMode.EUCLIDEAN:
                    m_heuristic = new HeuristicDelegate(Heuristic.Euclidean);
                    break;
                case HeuristicMode.CHEBYSHEV:
                    m_heuristic = new HeuristicDelegate(Heuristic.Chebyshev);
                    break;
                default:
                    m_heuristic = new HeuristicDelegate(Heuristic.Euclidean);
                    break;
            }
            m_allowEndNodeUnWalkable = iAllowEndNodeUnWalkable;
            m_crossAdjacentPoint = iCrossAdjacentPoint;
            m_crossCorner = iCrossCorner;

            openList = new List<Node>();

            m_searchGrid = iGrid;
            m_startNode = null;
            m_endNode = null;
            m_useRecursive = false;
        }

        public void SetHeuristic(HeuristicMode iMode)
        {
            m_heuristic = null;
            switch (iMode)
            {
                case HeuristicMode.MANHATTAN:
                    m_heuristic = new HeuristicDelegate(Heuristic.Manhattan);
                    break;
                case HeuristicMode.EUCLIDEAN:
                    m_heuristic = new HeuristicDelegate(Heuristic.Euclidean);
                    break;
                case HeuristicMode.CHEBYSHEV:
                    m_heuristic = new HeuristicDelegate(Heuristic.Chebyshev);
                    break;
                default:
                    m_heuristic = new HeuristicDelegate(Heuristic.Euclidean);
                    break;
            }
        }

        public void Reset(GridPos iStartPos, GridPos iEndPos, BaseGrid iSearchGrid = null)
        {
            openList.Clear();
            m_startNode = null;
            m_endNode = null;

            if (iSearchGrid != null)
                m_searchGrid = iSearchGrid;
            m_searchGrid.Reset();
            m_startNode = m_searchGrid.GetNodeAt(iStartPos.x, iStartPos.y);
            m_endNode = m_searchGrid.GetNodeAt(iEndPos.x, iEndPos.y);
            if (m_startNode == null)
                m_startNode = new Node(iStartPos.x, iStartPos.y, true);
            if (m_endNode == null)
                m_endNode = new Node(iEndPos.x, iEndPos.y, true);


        }

        public bool CrossAdjacentPoint
        {
            get
            {
                return m_crossCorner && m_crossAdjacentPoint;
            }
            set
            {
                m_crossAdjacentPoint = value;
            }
        }

        public bool CrossCorner
        {
            get
            {
                return m_crossCorner;
            }
            set
            {
                m_crossCorner = value;
            }
        }

        public bool AllowEndNodeUnWalkable
        {
            get
            {
                return m_allowEndNodeUnWalkable;
            }
            set
            {
                m_allowEndNodeUnWalkable = value;
            }
        }

        public HeuristicDelegate HeuristicFunc
        {
            get
            {
                return m_heuristic;
            }
        }

        public BaseGrid SearchGrid
        {
            get
            {
                return m_searchGrid;
            }
        }

        public Node StartNode
        {
            get
            {
                return m_startNode;
            }
        }
        public Node EndNode
        {
            get
            {
                return m_endNode;
            }
        }

        public bool UseRecursive
        {
            get
            {
                return m_useRecursive;
            }
            set
            {
                m_useRecursive = value;
            }
        }
        protected HeuristicDelegate m_heuristic;
        protected bool m_crossAdjacentPoint;
        protected bool m_crossCorner;
        protected bool m_allowEndNodeUnWalkable;

        protected bool m_useRecursive;

        protected BaseGrid m_searchGrid;
        protected Node m_startNode;
        protected Node m_endNode;

        public List<Node> openList;
    }
    class JumpPointFinder
    {
        public static List<GridPos> FindPath(JumpPointParam iParam)
        {

            List<Node> tOpenList = iParam.openList;
            Node tStartNode = iParam.StartNode;
            Node tEndNode = iParam.EndNode;
            Node tNode;
            bool revertEndNodeWalkable = false;

            // set the `g` and `f` value of the start node to be 0
            tStartNode.startToCurNodeLen = 0;
            tStartNode.heuristicStartToEndLen = 0;

            // push the start node into the open list
            tOpenList.Add(tStartNode);
            tStartNode.isOpened = true;

            if (iParam.AllowEndNodeUnWalkable && !iParam.SearchGrid.IsWalkableAt(tEndNode.x, tEndNode.y))
            {
                iParam.SearchGrid.SetWalkableAt(tEndNode.x, tEndNode.y, true);
                revertEndNodeWalkable = true;
            }

            // while the open list is not empty
            while (tOpenList.Count > 0)
            {
                // pop the position of node which has the minimum `f` value.
                tOpenList.Sort();
                tNode = (Node)tOpenList[0];
                tOpenList.RemoveAt(0);
                tNode.isClosed = true;

                if (tNode.Equals(tEndNode))
                {
                    if (revertEndNodeWalkable)
                    {
                        iParam.SearchGrid.SetWalkableAt(tEndNode.x, tEndNode.y, false);
                    }
                    return Node.Backtrace(tNode); // rebuilding path
                }

                identifySuccessors(iParam, tNode);
            }

            if (revertEndNodeWalkable)
            {
                iParam.SearchGrid.SetWalkableAt(tEndNode.x, tEndNode.y, false);
            }

            // fail to find the path
            return new List<GridPos>();
        }

        private static void identifySuccessors(JumpPointParam iParam, Node iNode)
        {
            HeuristicDelegate tHeuristic = iParam.HeuristicFunc;
            List<Node> tOpenList = iParam.openList;
            int tEndX = iParam.EndNode.x;
            int tEndY = iParam.EndNode.y;
            GridPos tNeighbor;
            GridPos? tJumpPoint;
            Node tJumpNode;

            List<GridPos> tNeighbors = findNeighbors(iParam, iNode);
            for (int i = 0; i < tNeighbors.Count; i++)
            {
                tNeighbor = tNeighbors[i];
                if (iParam.UseRecursive)
                    tJumpPoint = jump(iParam, tNeighbor.x, tNeighbor.y, iNode.x, iNode.y);
                else
                    tJumpPoint = jumpLoop(iParam, tNeighbor.x, tNeighbor.y, iNode.x, iNode.y);
                if (tJumpPoint != null)
                {
                    tJumpNode = iParam.SearchGrid.GetNodeAt(tJumpPoint.Value.x, tJumpPoint.Value.y);
                    if (tJumpNode == null)
                    {
                        if (iParam.EndNode.x == tJumpPoint.Value.x && iParam.EndNode.y == tJumpPoint.Value.y)
                            tJumpNode = iParam.SearchGrid.GetNodeAt(tJumpPoint.Value);
                    }
                    if (tJumpNode.isClosed)
                    {
                        continue;
                    }
                    // include distance, as parent may not be immediately adjacent:
                    float tCurNodeToJumpNodeLen = tHeuristic(Math.Abs(tJumpPoint.Value.x - iNode.x), Math.Abs(tJumpPoint.Value.y - iNode.y));
                    float tStartToJumpNodeLen = iNode.startToCurNodeLen + tCurNodeToJumpNodeLen; // next `startToCurNodeLen` value

                    if (!tJumpNode.isOpened || tStartToJumpNodeLen < tJumpNode.startToCurNodeLen)
                    {
                        tJumpNode.startToCurNodeLen = tStartToJumpNodeLen;
                        tJumpNode.heuristicCurNodeToEndLen = (tJumpNode.heuristicCurNodeToEndLen == null ? tHeuristic(Math.Abs(tJumpPoint.Value.x - tEndX), Math.Abs(tJumpPoint.Value.y - tEndY)) : tJumpNode.heuristicCurNodeToEndLen);
                        tJumpNode.heuristicStartToEndLen = tJumpNode.startToCurNodeLen + tJumpNode.heuristicCurNodeToEndLen.Value;
                        tJumpNode.parent = iNode;

                        if (!tJumpNode.isOpened)
                        {
                            tOpenList.Add(tJumpNode);
                            tJumpNode.isOpened = true;
                        }
                    }
                }
            }
        }

        private class JumpSnapshot
        {
            public int iX;
            public int iY;
            public int iPx;
            public int iPy;
            public int tDx;
            public int tDy;
            public GridPos? jx;
            public GridPos? jy;
            public int stage;
            public JumpSnapshot()
            {

                iX = 0;
                iY = 0;
                iPx = 0;
                iPy = 0;
                tDx = 0;
                tDy = 0;
                jx = null;
                jy = null;
                stage = 0;
            }
        }

        private static GridPos? jumpLoop(JumpPointParam iParam, int iX, int iY, int iPx, int iPy)
        {
            GridPos? retVal = null;
            Stack<JumpSnapshot> stack = new Stack<JumpSnapshot>();

            JumpSnapshot currentSnapshot = new JumpSnapshot();
            JumpSnapshot newSnapshot = null;
            currentSnapshot.iX = iX;
            currentSnapshot.iY = iY;
            currentSnapshot.iPx = iPx;
            currentSnapshot.iPy = iPy;
            currentSnapshot.stage = 0;

            stack.Push(currentSnapshot);
            while (stack.Count != 0)
            {
                currentSnapshot = stack.Pop();
                switch (currentSnapshot.stage)
                {
                    case 0:
                        if (!iParam.SearchGrid.IsWalkableAt(currentSnapshot.iX, currentSnapshot.iY))
                        {
                            retVal = null;
                            continue;
                        }
                        else if (iParam.SearchGrid.GetNodeAt(currentSnapshot.iX, currentSnapshot.iY).Equals(iParam.EndNode))
                        {
                            retVal = new GridPos(currentSnapshot.iX, currentSnapshot.iY);
                            continue;
                        }

                        currentSnapshot.tDx = currentSnapshot.iX - currentSnapshot.iPx;
                        currentSnapshot.tDy = currentSnapshot.iY - currentSnapshot.iPy;
                        currentSnapshot.jx = null;
                        currentSnapshot.jy = null;
                        if (iParam.CrossCorner)
                        {
                            // check for forced neighbors
                            // along the diagonal
                            if (currentSnapshot.tDx != 0 && currentSnapshot.tDy != 0)
                            {
                                if ((iParam.SearchGrid.IsWalkableAt(currentSnapshot.iX - currentSnapshot.tDx, currentSnapshot.iY + currentSnapshot.tDy) && !iParam.SearchGrid.IsWalkableAt(currentSnapshot.iX - currentSnapshot.tDx, currentSnapshot.iY)) ||
                                    (iParam.SearchGrid.IsWalkableAt(currentSnapshot.iX + currentSnapshot.tDx, currentSnapshot.iY - currentSnapshot.tDy) && !iParam.SearchGrid.IsWalkableAt(currentSnapshot.iX, currentSnapshot.iY - currentSnapshot.tDy)))
                                {
                                    retVal = new GridPos(currentSnapshot.iX, currentSnapshot.iY);
                                    continue;
                                }
                            }
                            // horizontally/vertically
                            else
                            {
                                if (currentSnapshot.tDx != 0)
                                {
                                    // moving along x
                                    if ((iParam.SearchGrid.IsWalkableAt(currentSnapshot.iX + currentSnapshot.tDx, currentSnapshot.iY + 1) && !iParam.SearchGrid.IsWalkableAt(currentSnapshot.iX, currentSnapshot.iY + 1)) ||
                                        (iParam.SearchGrid.IsWalkableAt(currentSnapshot.iX + currentSnapshot.tDx, currentSnapshot.iY - 1) && !iParam.SearchGrid.IsWalkableAt(currentSnapshot.iX, currentSnapshot.iY - 1)))
                                    {
                                        retVal = new GridPos(currentSnapshot.iX, currentSnapshot.iY);
                                        continue;
                                    }
                                }
                                else
                                {
                                    if ((iParam.SearchGrid.IsWalkableAt(currentSnapshot.iX + 1, currentSnapshot.iY + currentSnapshot.tDy) && !iParam.SearchGrid.IsWalkableAt(currentSnapshot.iX + 1, currentSnapshot.iY)) ||
                                        (iParam.SearchGrid.IsWalkableAt(currentSnapshot.iX - 1, currentSnapshot.iY + currentSnapshot.tDy) && !iParam.SearchGrid.IsWalkableAt(currentSnapshot.iX - 1, currentSnapshot.iY)))
                                    {
                                        retVal = new GridPos(currentSnapshot.iX, currentSnapshot.iY);
                                        continue;
                                    }
                                }
                            }
                            // when moving diagonally, must check for vertical/horizontal jump points
                            if (currentSnapshot.tDx != 0 && currentSnapshot.tDy != 0)
                            {
                                currentSnapshot.stage = 1;
                                stack.Push(currentSnapshot);

                                newSnapshot = new JumpSnapshot();
                                newSnapshot.iX = currentSnapshot.iX + currentSnapshot.tDx;
                                newSnapshot.iY = currentSnapshot.iY;
                                newSnapshot.iPx = currentSnapshot.iX;
                                newSnapshot.iPy = currentSnapshot.iY;
                                newSnapshot.stage = 0;
                                stack.Push(newSnapshot);
                                continue;
                            }

                            // moving diagonally, must make sure one of the vertical/horizontal
                            // neighbors is open to allow the path

                            // moving diagonally, must make sure one of the vertical/horizontal
                            // neighbors is open to allow the path
                            if (iParam.SearchGrid.IsWalkableAt(currentSnapshot.iX + currentSnapshot.tDx, currentSnapshot.iY) || iParam.SearchGrid.IsWalkableAt(currentSnapshot.iX, currentSnapshot.iY + currentSnapshot.tDy))
                            {
                                newSnapshot = new JumpSnapshot();
                                newSnapshot.iX = currentSnapshot.iX + currentSnapshot.tDx;
                                newSnapshot.iY = currentSnapshot.iY + currentSnapshot.tDy;
                                newSnapshot.iPx = currentSnapshot.iX;
                                newSnapshot.iPy = currentSnapshot.iY;
                                newSnapshot.stage = 0;
                                stack.Push(newSnapshot);
                                continue;
                            }
                            else if (iParam.CrossAdjacentPoint)
                            {
                                newSnapshot = new JumpSnapshot();
                                newSnapshot.iX = currentSnapshot.iX + currentSnapshot.tDx;
                                newSnapshot.iY = currentSnapshot.iY + currentSnapshot.tDy;
                                newSnapshot.iPx = currentSnapshot.iX;
                                newSnapshot.iPy = currentSnapshot.iY;
                                newSnapshot.stage = 0;
                                stack.Push(newSnapshot);
                                continue;
                            }
                        }
                        else //if (!iParam.CrossCorner)
                        {
                            // check for forced neighbors
                            // along the diagonal
                            if (currentSnapshot.tDx != 0 && currentSnapshot.tDy != 0)
                            {
                                if ((iParam.SearchGrid.IsWalkableAt(currentSnapshot.iX + currentSnapshot.tDx, currentSnapshot.iY + currentSnapshot.tDy) && iParam.SearchGrid.IsWalkableAt(currentSnapshot.iX, currentSnapshot.iY + currentSnapshot.tDy) && !iParam.SearchGrid.IsWalkableAt(currentSnapshot.iX + currentSnapshot.tDx, currentSnapshot.iY)) ||
                                    (iParam.SearchGrid.IsWalkableAt(currentSnapshot.iX + currentSnapshot.tDx, currentSnapshot.iY + currentSnapshot.tDy) && iParam.SearchGrid.IsWalkableAt(currentSnapshot.iX + currentSnapshot.tDx, currentSnapshot.iY) && !iParam.SearchGrid.IsWalkableAt(currentSnapshot.iX, currentSnapshot.iY + currentSnapshot.tDy)))
                                {
                                    retVal = new GridPos(currentSnapshot.iX, currentSnapshot.iY);
                                    continue;
                                }
                            }
                            // horizontally/vertically
                            else
                            {
                                if (currentSnapshot.tDx != 0)
                                {
                                    // moving along x
                                    if ((iParam.SearchGrid.IsWalkableAt(currentSnapshot.iX, currentSnapshot.iY + 1) && !iParam.SearchGrid.IsWalkableAt(currentSnapshot.iX - currentSnapshot.tDx, currentSnapshot.iY + 1)) ||
                                        (iParam.SearchGrid.IsWalkableAt(currentSnapshot.iX, currentSnapshot.iY - 1) && !iParam.SearchGrid.IsWalkableAt(currentSnapshot.iX - currentSnapshot.tDx, currentSnapshot.iY - 1)))
                                    {
                                        retVal = new GridPos(currentSnapshot.iX, currentSnapshot.iY);
                                        continue;
                                    }
                                }
                                else
                                {
                                    if ((iParam.SearchGrid.IsWalkableAt(currentSnapshot.iX + 1, currentSnapshot.iY) && !iParam.SearchGrid.IsWalkableAt(currentSnapshot.iX + 1, currentSnapshot.iY - currentSnapshot.tDy)) ||
                                        (iParam.SearchGrid.IsWalkableAt(currentSnapshot.iX - 1, currentSnapshot.iY) && !iParam.SearchGrid.IsWalkableAt(currentSnapshot.iX - 1, currentSnapshot.iY - currentSnapshot.tDy)))
                                    {
                                        retVal = new GridPos(currentSnapshot.iX, currentSnapshot.iY);
                                        continue;
                                    }
                                }
                            }


                            // when moving diagonally, must check for vertical/horizontal jump points
                            if (currentSnapshot.tDx != 0 && currentSnapshot.tDy != 0)
                            {
                                currentSnapshot.stage = 3;
                                stack.Push(currentSnapshot);

                                newSnapshot = new JumpSnapshot();
                                newSnapshot.iX = currentSnapshot.iX + currentSnapshot.tDx;
                                newSnapshot.iY = currentSnapshot.iY;
                                newSnapshot.iPx = currentSnapshot.iX;
                                newSnapshot.iPy = currentSnapshot.iY;
                                newSnapshot.stage = 0;
                                stack.Push(newSnapshot);
                                continue;
                            }

                            // moving diagonally, must make sure both of the vertical/horizontal
                            // neighbors is open to allow the path
                            if (iParam.SearchGrid.IsWalkableAt(currentSnapshot.iX + currentSnapshot.tDx, currentSnapshot.iY) && iParam.SearchGrid.IsWalkableAt(currentSnapshot.iX, currentSnapshot.iY + currentSnapshot.tDy))
                            {
                                newSnapshot = new JumpSnapshot();
                                newSnapshot.iX = currentSnapshot.iX + currentSnapshot.tDx;
                                newSnapshot.iY = currentSnapshot.iY + currentSnapshot.tDy;
                                newSnapshot.iPx = currentSnapshot.iX;
                                newSnapshot.iPy = currentSnapshot.iY;
                                newSnapshot.stage = 0;
                                stack.Push(newSnapshot);
                                continue;
                            }
                        }
                        retVal = null;
                        break;
                    case 1:
                        currentSnapshot.jx = retVal;

                        currentSnapshot.stage = 2;
                        stack.Push(currentSnapshot);

                        newSnapshot = new JumpSnapshot();
                        newSnapshot.iX = currentSnapshot.iX;
                        newSnapshot.iY = currentSnapshot.iY + currentSnapshot.tDy;
                        newSnapshot.iPx = currentSnapshot.iX;
                        newSnapshot.iPy = currentSnapshot.iY;
                        newSnapshot.stage = 0;
                        stack.Push(newSnapshot);
                        break;
                    case 2:
                        currentSnapshot.jy = retVal;
                        if (currentSnapshot.jx != null || currentSnapshot.jy != null)
                        {
                            retVal = new GridPos(currentSnapshot.iX, currentSnapshot.iY);
                            continue;
                        }

                        // moving diagonally, must make sure one of the vertical/horizontal
                        // neighbors is open to allow the path
                        if (iParam.SearchGrid.IsWalkableAt(currentSnapshot.iX + currentSnapshot.tDx, currentSnapshot.iY) || iParam.SearchGrid.IsWalkableAt(currentSnapshot.iX, currentSnapshot.iY + currentSnapshot.tDy))
                        {
                            newSnapshot = new JumpSnapshot();
                            newSnapshot.iX = currentSnapshot.iX + currentSnapshot.tDx;
                            newSnapshot.iY = currentSnapshot.iY + currentSnapshot.tDy;
                            newSnapshot.iPx = currentSnapshot.iX;
                            newSnapshot.iPy = currentSnapshot.iY;
                            newSnapshot.stage = 0;
                            stack.Push(newSnapshot);
                            continue;
                        }
                        else if (iParam.CrossAdjacentPoint)
                        {
                            newSnapshot = new JumpSnapshot();
                            newSnapshot.iX = currentSnapshot.iX + currentSnapshot.tDx;
                            newSnapshot.iY = currentSnapshot.iY + currentSnapshot.tDy;
                            newSnapshot.iPx = currentSnapshot.iX;
                            newSnapshot.iPy = currentSnapshot.iY;
                            newSnapshot.stage = 0;
                            stack.Push(newSnapshot);
                            continue;
                        }
                        retVal = null;
                        break;
                    case 3:
                        currentSnapshot.jx = retVal;

                        currentSnapshot.stage = 4;
                        stack.Push(currentSnapshot);

                        newSnapshot = new JumpSnapshot();
                        newSnapshot.iX = currentSnapshot.iX;
                        newSnapshot.iY = currentSnapshot.iY + currentSnapshot.tDy;
                        newSnapshot.iPx = currentSnapshot.iX;
                        newSnapshot.iPy = currentSnapshot.iY;
                        newSnapshot.stage = 0;
                        stack.Push(newSnapshot);
                        break;
                    case 4:
                        currentSnapshot.jy = retVal;
                        if (currentSnapshot.jx != null || currentSnapshot.jy != null)
                        {
                            retVal = new GridPos(currentSnapshot.iX, currentSnapshot.iY);
                            continue;
                        }

                        // moving diagonally, must make sure both of the vertical/horizontal
                        // neighbors is open to allow the path
                        if (iParam.SearchGrid.IsWalkableAt(currentSnapshot.iX + currentSnapshot.tDx, currentSnapshot.iY) && iParam.SearchGrid.IsWalkableAt(currentSnapshot.iX, currentSnapshot.iY + currentSnapshot.tDy))
                        {
                            newSnapshot = new JumpSnapshot();
                            newSnapshot.iX = currentSnapshot.iX + currentSnapshot.tDx;
                            newSnapshot.iY = currentSnapshot.iY + currentSnapshot.tDy;
                            newSnapshot.iPx = currentSnapshot.iX;
                            newSnapshot.iPy = currentSnapshot.iY;
                            newSnapshot.stage = 0;
                            stack.Push(newSnapshot);
                            continue;
                        }
                        retVal = null;
                        break;
                }
            }

            return retVal;

        }
        private static GridPos? jump(JumpPointParam iParam, int iX, int iY, int iPx, int iPy)
        {
            if (!iParam.SearchGrid.IsWalkableAt(iX, iY))
            {
                return null;
            }
            else if (iParam.SearchGrid.GetNodeAt(iX, iY).Equals(iParam.EndNode))
            {
                return new GridPos(iX, iY);
            }

            int tDx = iX - iPx;
            int tDy = iY - iPy;
            GridPos? jx = null;
            GridPos? jy = null;
            if (iParam.CrossCorner)
            {
                // check for forced neighbors
                // along the diagonal
                if (tDx != 0 && tDy != 0)
                {
                    if ((iParam.SearchGrid.IsWalkableAt(iX - tDx, iY + tDy) && !iParam.SearchGrid.IsWalkableAt(iX - tDx, iY)) ||
                        (iParam.SearchGrid.IsWalkableAt(iX + tDx, iY - tDy) && !iParam.SearchGrid.IsWalkableAt(iX, iY - tDy)))
                    {
                        return new GridPos(iX, iY);
                    }
                }
                // horizontally/vertically
                else
                {
                    if (tDx != 0)
                    {
                        // moving along x
                        if ((iParam.SearchGrid.IsWalkableAt(iX + tDx, iY + 1) && !iParam.SearchGrid.IsWalkableAt(iX, iY + 1)) ||
                            (iParam.SearchGrid.IsWalkableAt(iX + tDx, iY - 1) && !iParam.SearchGrid.IsWalkableAt(iX, iY - 1)))
                        {
                            return new GridPos(iX, iY);
                        }
                    }
                    else
                    {
                        if ((iParam.SearchGrid.IsWalkableAt(iX + 1, iY + tDy) && !iParam.SearchGrid.IsWalkableAt(iX + 1, iY)) ||
                            (iParam.SearchGrid.IsWalkableAt(iX - 1, iY + tDy) && !iParam.SearchGrid.IsWalkableAt(iX - 1, iY)))
                        {
                            return new GridPos(iX, iY);
                        }
                    }
                }
                // when moving diagonally, must check for vertical/horizontal jump points
                if (tDx != 0 && tDy != 0)
                {
                    jx = jump(iParam, iX + tDx, iY, iX, iY);
                    jy = jump(iParam, iX, iY + tDy, iX, iY);
                    if (jx != null || jy != null)
                    {
                        return new GridPos(iX, iY);
                    }
                }

                // moving diagonally, must make sure one of the vertical/horizontal
                // neighbors is open to allow the path
                if (iParam.SearchGrid.IsWalkableAt(iX + tDx, iY) || iParam.SearchGrid.IsWalkableAt(iX, iY + tDy))
                {
                    return jump(iParam, iX + tDx, iY + tDy, iX, iY);
                }
                else if (iParam.CrossAdjacentPoint)
                {
                    return jump(iParam, iX + tDx, iY + tDy, iX, iY);
                }
                else
                {
                    return null;
                }
            }
            else //if (!iParam.CrossCorner)
            {
                // check for forced neighbors
                // along the diagonal
                if (tDx != 0 && tDy != 0)
                {
                    if ((iParam.SearchGrid.IsWalkableAt(iX + tDx, iY + tDy) && iParam.SearchGrid.IsWalkableAt(iX, iY + tDy) && !iParam.SearchGrid.IsWalkableAt(iX + tDx, iY)) ||
                        (iParam.SearchGrid.IsWalkableAt(iX + tDx, iY + tDy) && iParam.SearchGrid.IsWalkableAt(iX + tDx, iY) && !iParam.SearchGrid.IsWalkableAt(iX, iY + tDy)))
                    {
                        return new GridPos(iX, iY);
                    }
                }
                // horizontally/vertically
                else
                {
                    if (tDx != 0)
                    {
                        // moving along x
                        if ((iParam.SearchGrid.IsWalkableAt(iX, iY + 1) && !iParam.SearchGrid.IsWalkableAt(iX - tDx, iY + 1)) ||
                            (iParam.SearchGrid.IsWalkableAt(iX, iY - 1) && !iParam.SearchGrid.IsWalkableAt(iX - tDx, iY - 1)))
                        {
                            return new GridPos(iX, iY);
                        }
                    }
                    else
                    {
                        if ((iParam.SearchGrid.IsWalkableAt(iX + 1, iY) && !iParam.SearchGrid.IsWalkableAt(iX + 1, iY - tDy)) ||
                            (iParam.SearchGrid.IsWalkableAt(iX - 1, iY) && !iParam.SearchGrid.IsWalkableAt(iX - 1, iY - tDy)))
                        {
                            return new GridPos(iX, iY);
                        }
                    }
                }


                // when moving diagonally, must check for vertical/horizontal jump points
                if (tDx != 0 && tDy != 0)
                {
                    jx = jump(iParam, iX + tDx, iY, iX, iY);
                    jy = jump(iParam, iX, iY + tDy, iX, iY);
                    if (jx != null || jy != null)
                    {
                        return new GridPos(iX, iY);
                    }
                }

                // moving diagonally, must make sure both of the vertical/horizontal
                // neighbors is open to allow the path
                if (iParam.SearchGrid.IsWalkableAt(iX + tDx, iY) && iParam.SearchGrid.IsWalkableAt(iX, iY + tDy))
                {
                    return jump(iParam, iX + tDx, iY + tDy, iX, iY);
                }
                else
                {
                    return null;
                }
            }

        }

        private static List<GridPos> findNeighbors(JumpPointParam iParam, Node iNode)
        {
            Node tParent = (Node)iNode.parent;
            int tX = iNode.x;
            int tY = iNode.y;
            int tPx, tPy, tDx, tDy;
            List<GridPos> tNeighbors = new List<GridPos>();
            List<Node> tNeighborNodes;
            Node tNeighborNode;

            // directed pruning: can ignore most neighbors, unless forced.
            if (tParent != null)
            {
                tPx = tParent.x;
                tPy = tParent.y;
                // get the normalized direction of travel
                tDx = (tX - tPx) / Math.Max(Math.Abs(tX - tPx), 1);
                tDy = (tY - tPy) / Math.Max(Math.Abs(tY - tPy), 1);

                if (iParam.CrossCorner)
                {
                    // search diagonally
                    if (tDx != 0 && tDy != 0)
                    {
                        if (iParam.SearchGrid.IsWalkableAt(tX, tY + tDy))
                        {
                            tNeighbors.Add(new GridPos(tX, tY + tDy));
                        }
                        if (iParam.SearchGrid.IsWalkableAt(tX + tDx, tY))
                        {
                            tNeighbors.Add(new GridPos(tX + tDx, tY));
                        }

                        if (iParam.SearchGrid.IsWalkableAt(tX + tDx, tY + tDy))
                        {
                            if (iParam.SearchGrid.IsWalkableAt(tX, tY + tDy) || iParam.SearchGrid.IsWalkableAt(tX + tDx, tY))
                            {
                                tNeighbors.Add(new GridPos(tX + tDx, tY + tDy));
                            }
                            else if (iParam.CrossAdjacentPoint)
                            {
                                tNeighbors.Add(new GridPos(tX + tDx, tY + tDy));
                            }
                        }

                        if (iParam.SearchGrid.IsWalkableAt(tX - tDx, tY + tDy))
                        {
                            if (iParam.SearchGrid.IsWalkableAt(tX, tY + tDy) && !iParam.SearchGrid.IsWalkableAt(tX - tDx, tY))
                            {
                                tNeighbors.Add(new GridPos(tX - tDx, tY + tDy));
                            }
                        }

                        if (iParam.SearchGrid.IsWalkableAt(tX + tDx, tY - tDy))
                        {
                            if (iParam.SearchGrid.IsWalkableAt(tX + tDx, tY) && !iParam.SearchGrid.IsWalkableAt(tX, tY - tDy))
                            {
                                tNeighbors.Add(new GridPos(tX + tDx, tY - tDy));
                            }
                        }


                    }
                    // search horizontally/vertically
                    else
                    {
                        if (tDx == 0)
                        {
                            if (iParam.SearchGrid.IsWalkableAt(tX, tY + tDy))
                            {
                                tNeighbors.Add(new GridPos(tX, tY + tDy));

                                if (iParam.SearchGrid.IsWalkableAt(tX + 1, tY + tDy) && !iParam.SearchGrid.IsWalkableAt(tX + 1, tY))
                                {
                                    tNeighbors.Add(new GridPos(tX + 1, tY + tDy));
                                }
                                if (iParam.SearchGrid.IsWalkableAt(tX - 1, tY + tDy) && !iParam.SearchGrid.IsWalkableAt(tX - 1, tY))
                                {
                                    tNeighbors.Add(new GridPos(tX - 1, tY + tDy));
                                }
                            }
                            else if (iParam.CrossAdjacentPoint)
                            {
                                if (iParam.SearchGrid.IsWalkableAt(tX + 1, tY + tDy) && !iParam.SearchGrid.IsWalkableAt(tX + 1, tY))
                                {
                                    tNeighbors.Add(new GridPos(tX + 1, tY + tDy));
                                }
                                if (iParam.SearchGrid.IsWalkableAt(tX - 1, tY + tDy) && !iParam.SearchGrid.IsWalkableAt(tX - 1, tY))
                                {
                                    tNeighbors.Add(new GridPos(tX - 1, tY + tDy));
                                }
                            }
                        }
                        else
                        {
                            if (iParam.SearchGrid.IsWalkableAt(tX + tDx, tY))
                            {

                                tNeighbors.Add(new GridPos(tX + tDx, tY));

                                if (iParam.SearchGrid.IsWalkableAt(tX + tDx, tY + 1) && !iParam.SearchGrid.IsWalkableAt(tX, tY + 1))
                                {
                                    tNeighbors.Add(new GridPos(tX + tDx, tY + 1));
                                }
                                if (iParam.SearchGrid.IsWalkableAt(tX + tDx, tY - 1) && !iParam.SearchGrid.IsWalkableAt(tX, tY - 1))
                                {
                                    tNeighbors.Add(new GridPos(tX + tDx, tY - 1));
                                }
                            }
                            else if (iParam.CrossAdjacentPoint)
                            {
                                if (iParam.SearchGrid.IsWalkableAt(tX + tDx, tY + 1) && !iParam.SearchGrid.IsWalkableAt(tX, tY + 1))
                                {
                                    tNeighbors.Add(new GridPos(tX + tDx, tY + 1));
                                }
                                if (iParam.SearchGrid.IsWalkableAt(tX + tDx, tY - 1) && !iParam.SearchGrid.IsWalkableAt(tX, tY - 1))
                                {
                                    tNeighbors.Add(new GridPos(tX + tDx, tY - 1));
                                }
                            }
                        }
                    }
                }
                else // if(!iParam.CrossCorner)
                {
                    // search diagonally
                    if (tDx != 0 && tDy != 0)
                    {
                        if (iParam.SearchGrid.IsWalkableAt(tX, tY + tDy))
                        {
                            tNeighbors.Add(new GridPos(tX, tY + tDy));
                        }
                        if (iParam.SearchGrid.IsWalkableAt(tX + tDx, tY))
                        {
                            tNeighbors.Add(new GridPos(tX + tDx, tY));
                        }

                        if (iParam.SearchGrid.IsWalkableAt(tX + tDx, tY + tDy))
                        {
                            if (iParam.SearchGrid.IsWalkableAt(tX, tY + tDy) && iParam.SearchGrid.IsWalkableAt(tX + tDx, tY))
                                tNeighbors.Add(new GridPos(tX + tDx, tY + tDy));
                        }

                        if (iParam.SearchGrid.IsWalkableAt(tX - tDx, tY + tDy))
                        {
                            if (iParam.SearchGrid.IsWalkableAt(tX, tY + tDy) && iParam.SearchGrid.IsWalkableAt(tX - tDx, tY))
                                tNeighbors.Add(new GridPos(tX - tDx, tY + tDy));
                        }

                        if (iParam.SearchGrid.IsWalkableAt(tX + tDx, tY - tDy))
                        {
                            if (iParam.SearchGrid.IsWalkableAt(tX, tY - tDy) && iParam.SearchGrid.IsWalkableAt(tX + tDx, tY))
                                tNeighbors.Add(new GridPos(tX + tDx, tY - tDy));
                        }


                    }
                    // search horizontally/vertically
                    else
                    {
                        if (tDx == 0)
                        {
                            if (iParam.SearchGrid.IsWalkableAt(tX, tY + tDy))
                            {
                                tNeighbors.Add(new GridPos(tX, tY + tDy));

                                if (iParam.SearchGrid.IsWalkableAt(tX + 1, tY + tDy) && iParam.SearchGrid.IsWalkableAt(tX + 1, tY))
                                {
                                    tNeighbors.Add(new GridPos(tX + 1, tY + tDy));
                                }
                                if (iParam.SearchGrid.IsWalkableAt(tX - 1, tY + tDy) && iParam.SearchGrid.IsWalkableAt(tX - 1, tY))
                                {
                                    tNeighbors.Add(new GridPos(tX - 1, tY + tDy));
                                }
                            }
                            if (iParam.SearchGrid.IsWalkableAt(tX + 1, tY))
                                tNeighbors.Add(new GridPos(tX + 1, tY));
                            if (iParam.SearchGrid.IsWalkableAt(tX - 1, tY))
                                tNeighbors.Add(new GridPos(tX - 1, tY));
                        }
                        else
                        {
                            if (iParam.SearchGrid.IsWalkableAt(tX + tDx, tY))
                            {

                                tNeighbors.Add(new GridPos(tX + tDx, tY));

                                if (iParam.SearchGrid.IsWalkableAt(tX + tDx, tY + 1) && iParam.SearchGrid.IsWalkableAt(tX, tY + 1))
                                {
                                    tNeighbors.Add(new GridPos(tX + tDx, tY + 1));
                                }
                                if (iParam.SearchGrid.IsWalkableAt(tX + tDx, tY - 1) && iParam.SearchGrid.IsWalkableAt(tX, tY - 1))
                                {
                                    tNeighbors.Add(new GridPos(tX + tDx, tY - 1));
                                }
                            }
                            if (iParam.SearchGrid.IsWalkableAt(tX, tY + 1))
                                tNeighbors.Add(new GridPos(tX, tY + 1));
                            if (iParam.SearchGrid.IsWalkableAt(tX, tY - 1))
                                tNeighbors.Add(new GridPos(tX, tY - 1));
                        }
                    }
                }

            }
            // return all neighbors
            else
            {
                tNeighborNodes = iParam.SearchGrid.GetNeighbors(iNode, iParam.CrossCorner, iParam.CrossAdjacentPoint);
                for (int i = 0; i < tNeighborNodes.Count; i++)
                {
                    tNeighborNode = tNeighborNodes[i];
                    tNeighbors.Add(new GridPos(tNeighborNode.x, tNeighborNode.y));
                }
            }

            return tNeighbors;
        }
    }
    public enum HeuristicMode
    {
        MANHATTAN,
        EUCLIDEAN,
        CHEBYSHEV,

    };
    public class Heuristic
    {
        public static float Manhattan(int iDx, int iDy)
        {
            return (float)iDx + iDy;
        }

        public static float Euclidean(int iDx, int iDy)
        {
            float tFdx = (float)iDx;
            float tFdy = (float)iDy;
            return (float)Math.Sqrt((double)(tFdx * tFdx + tFdy * tFdy));
        }

        public static float Chebyshev(int iDx, int iDy)
        {
            return (float)Math.Max(iDx, iDy);
        }

    }
    public class Node : IComparable
    {
        public int x;
        public int y;
        public bool walkable;
        public float heuristicStartToEndLen; // which passes current node
        public float startToCurNodeLen;
        public float? heuristicCurNodeToEndLen;
        public bool isOpened;
        public bool isClosed;
        public Object parent;

        public Node(int iX, int iY, bool? iWalkable = null)
        {
            this.x = iX;
            this.y = iY;
            this.walkable = (iWalkable.HasValue ? iWalkable.Value : false);
            this.heuristicStartToEndLen = 0;
            this.startToCurNodeLen = 0;
            this.heuristicCurNodeToEndLen = null;
            this.isOpened = false;
            this.isClosed = false;
            this.parent = null;

        }

        public void Reset(bool? iWalkable = null)
        {
            if (iWalkable.HasValue)
                walkable = iWalkable.Value;
            this.heuristicStartToEndLen = 0;
            this.startToCurNodeLen = 0;
            this.heuristicCurNodeToEndLen = null;
            this.isOpened = false;
            this.isClosed = false;
            this.parent = null;
        }


        public int CompareTo(object iObj)
        {
            Node tOtherNode = (Node)iObj;
            float result = this.heuristicStartToEndLen - tOtherNode.heuristicStartToEndLen;
            if (result > 0.0f)
                return 1;
            else if (result == 0.0f)
                return 0;
            return -1;
        }

        public static List<GridPos> Backtrace(Node iNode)
        {
            List<GridPos> path = new List<GridPos>();
            path.Add(new GridPos(iNode.x, iNode.y));
            while (iNode.parent != null)
            {
                iNode = (Node)iNode.parent;
                path.Add(new GridPos(iNode.x, iNode.y));
            }
            path.Reverse();
            return path;
        }


        public override int GetHashCode()
        {
            return x ^ y;
        }

        public override bool Equals(System.Object obj)
        {
            // If parameter is null return false.
            if (obj == null)
            {
                return false;
            }

            // If parameter cannot be cast to Point return false.
            Node p = obj as Node;
            if ((System.Object)p == null)
            {
                return false;
            }

            // Return true if the fields match:
            return (x == p.x) && (y == p.y);
        }

        public bool Equals(Node p)
        {
            // If parameter is null return false:
            if ((object)p == null)
            {
                return false;
            }

            // Return true if the fields match:
            return (x == p.x) && (y == p.y);
        }

        public static bool operator ==(Node a, Node b)
        {
            // If both are null, or both are same instance, return true.
            if (System.Object.ReferenceEquals(a, b))
            {
                return true;
            }

            // If one is null, but not both, return false.
            if (((object)a == null) || ((object)b == null))
            {
                return false;
            }

            // Return true if the fields match:
            return a.x == b.x && a.y == b.y;
        }

        public static bool operator !=(Node a, Node b)
        {
            return !(a == b);
        }

    }
    public class StaticGrid : BaseGrid
    {
        public override int width { get; protected set; }

        public override int height { get; protected set; }

        private Node[][] m_nodes;

        public StaticGrid(int iWidth, int iHeight, bool[,] iMatrix = null) : base()
        {
            width = iWidth;
            height = iHeight;
            m_gridRect.minX = 0;
            m_gridRect.minY = 0;
            m_gridRect.maxX = iWidth - 1;
            m_gridRect.maxY = iHeight - 1;
            this.m_nodes = buildNodes(iWidth, iHeight, iMatrix);
        }


        private Node[][] buildNodes(int iWidth, int iHeight, bool[,] iMatrix)
        {

            Node[][] tNodes = new Node[iWidth][];
            for (int widthTrav = 0; widthTrav < iWidth; widthTrav++)
            {
                tNodes[widthTrav] = new Node[iHeight];
                for (int heightTrav = 0; heightTrav < iHeight; heightTrav++)
                {
                    tNodes[widthTrav][heightTrav] = new Node(widthTrav, heightTrav, null);
                }
            }

            if (iMatrix == null)
            {
                return tNodes;
            }

            for (int widthTrav = 0; widthTrav < iWidth; widthTrav++)
            {
                for (int heightTrav = 0; heightTrav < iHeight; heightTrav++)
                {
                    if (!iMatrix[widthTrav,heightTrav])
                    {
                        tNodes[widthTrav][heightTrav].walkable = true;
                    }
                    else
                    {
                        tNodes[widthTrav][heightTrav].walkable = false;
                    }
                }
            }
            return tNodes;
        }

        public override Node GetNodeAt(int iX, int iY)
        {
            return this.m_nodes[iX][iY];
        }

        public override bool IsWalkableAt(int iX, int iY)
        {
            return isInside(iX, iY) && this.m_nodes[iX][iY].walkable;
        }

        protected bool isInside(int iX, int iY)
        {
            return (iX >= 0 && iX < width) && (iY >= 0 && iY < height);
        }

        public override bool SetWalkableAt(int iX, int iY, bool iWalkable)
        {
            this.m_nodes[iX][iY].walkable = iWalkable;
            return true;
        }

        protected bool isInside(GridPos iPos)
        {
            return isInside(iPos.x, iPos.y);
        }

        public override Node GetNodeAt(GridPos iPos)
        {
            return GetNodeAt(iPos.x, iPos.y);
        }

        public override bool IsWalkableAt(GridPos iPos)
        {
            return IsWalkableAt(iPos.x, iPos.y);
        }

        public override bool SetWalkableAt(GridPos iPos, bool iWalkable)
        {
            return SetWalkableAt(iPos.x, iPos.y, iWalkable);
        }

        public override void Reset()
        {
            Reset(null);
        }

        public void Reset(bool[][] iMatrix)
        {
            for (int widthTrav = 0; widthTrav < width; widthTrav++)
            {
                for (int heightTrav = 0; heightTrav < height; heightTrav++)
                {
                    m_nodes[widthTrav][heightTrav].Reset();
                }
            }

            if (iMatrix == null)
            {
                return;
            }
            if (iMatrix.Length != width || iMatrix[0].Length != height)
            {
                throw new System.ApplicationException("Matrix size does not fit");
            }

            for (int widthTrav = 0; widthTrav < width; widthTrav++)
            {
                for (int heightTrav = 0; heightTrav < height; heightTrav++)
                {
                    if (iMatrix[widthTrav][heightTrav])
                    {
                        m_nodes[widthTrav][heightTrav].walkable = true;
                    }
                    else
                    {
                        m_nodes[widthTrav][heightTrav].walkable = false;
                    }
                }
            }
        }

        public override BaseGrid Clone()
        {
            int tWidth = width;
            int tHeight = height;
            Node[][] tNodes = this.m_nodes;

            StaticGrid tNewGrid = new StaticGrid(tWidth, tHeight, null);

            Node[][] tNewNodes = new Node[tWidth][];
            for (int widthTrav = 0; widthTrav < tWidth; widthTrav++)
            {
                tNewNodes[widthTrav] = new Node[tHeight];
                for (int heightTrav = 0; heightTrav < tHeight; heightTrav++)
                {
                    tNewNodes[widthTrav][heightTrav] = new Node(widthTrav, heightTrav, tNodes[widthTrav][heightTrav].walkable);
                }
            }
            tNewGrid.m_nodes = tNewNodes;

            return tNewGrid;
        }
    }
    public class DynamicGrid : BaseGrid
    {
        protected Dictionary<GridPos, Node> m_nodes;
        private bool m_notSet;


        public override int width
        {
            get
            {
                if (m_notSet)
                    setBoundingBox();
                return m_gridRect.maxX - m_gridRect.minX;
            }
            protected set
            {

            }
        }

        public override int height
        {
            get
            {
                if (m_notSet)
                    setBoundingBox();
                return m_gridRect.maxY - m_gridRect.minY;
            }
            protected set
            {

            }
        }

        public DynamicGrid(List<GridPos> iWalkableGridList = null)
            : base()
        {
            m_gridRect = new GridRect();
            m_gridRect.minX = 0;
            m_gridRect.minY = 0;
            m_gridRect.maxX = 0;
            m_gridRect.maxY = 0;
            m_notSet = true;
            buildNodes(iWalkableGridList);
        }

        protected void buildNodes(List<GridPos> iWalkableGridList)
        {

            m_nodes = new Dictionary<GridPos, Node>();
            if (iWalkableGridList == null)
                return;
            foreach (GridPos gridPos in iWalkableGridList)
            {
                SetWalkableAt(gridPos.x, gridPos.y, true);
            }
        }


        public override Node GetNodeAt(int iX, int iY)
        {
            GridPos pos = new GridPos(iX, iY);
            return GetNodeAt(pos);
        }

        public override bool IsWalkableAt(int iX, int iY)
        {
            GridPos pos = new GridPos(iX, iY);
            return IsWalkableAt(pos);
        }

        private void setBoundingBox()
        {
            m_notSet = true;
            foreach (KeyValuePair<GridPos, Node> pair in m_nodes)
            {
                if (pair.Key.x < m_gridRect.minX || m_notSet)
                    m_gridRect.minX = pair.Key.x;
                if (pair.Key.x > m_gridRect.maxX || m_notSet)
                    m_gridRect.maxX = pair.Key.x;
                if (pair.Key.y < m_gridRect.minY || m_notSet)
                    m_gridRect.minY = pair.Key.y;
                if (pair.Key.y > m_gridRect.maxY || m_notSet)
                    m_gridRect.maxY = pair.Key.y;
                m_notSet = false;
            }
            m_notSet = false;
        }

        public override bool SetWalkableAt(int iX, int iY, bool iWalkable)
        {
            GridPos pos = new GridPos(iX, iY);

            if (iWalkable)
            {
                if (m_nodes.ContainsKey(pos))
                {
                    // this.m_nodes[pos].walkable = iWalkable;
                    return true;
                }
                else
                {
                    if (iX < m_gridRect.minX || m_notSet)
                        m_gridRect.minX = iX;
                    if (iX > m_gridRect.maxX || m_notSet)
                        m_gridRect.maxX = iX;
                    if (iY < m_gridRect.minY || m_notSet)
                        m_gridRect.minY = iY;
                    if (iY > m_gridRect.maxY || m_notSet)
                        m_gridRect.maxY = iY;
                    m_nodes.Add(new GridPos(pos.x, pos.y), new Node(pos.x, pos.y, iWalkable));
                    m_notSet = false;
                }
            }
            else
            {
                if (m_nodes.ContainsKey(pos))
                {
                    m_nodes.Remove(pos);
                    if (iX == m_gridRect.minX || iX == m_gridRect.maxX || iY == m_gridRect.minY || iY == m_gridRect.maxY)
                        m_notSet = true;
                }
            }
            return true;
        }

        public override Node GetNodeAt(GridPos iPos)
        {
            if (m_nodes.ContainsKey(iPos))
            {
                return m_nodes[iPos];
            }
            return null;
        }

        public override bool IsWalkableAt(GridPos iPos)
        {
            return m_nodes.ContainsKey(iPos);
        }

        public override bool SetWalkableAt(GridPos iPos, bool iWalkable)
        {
            return SetWalkableAt(iPos.x, iPos.y, iWalkable);
        }

        public override void Reset()
        {
            Reset(null);
        }

        public void Reset(List<GridPos> iWalkableGridList)
        {

            foreach (KeyValuePair<GridPos, Node> keyValue in m_nodes)
            {
                keyValue.Value.Reset();
            }

            if (iWalkableGridList == null)
                return;
            foreach (KeyValuePair<GridPos, Node> keyValue in m_nodes)
            {
                if (iWalkableGridList.Contains(keyValue.Key))
                    SetWalkableAt(keyValue.Key, true);
                else
                    SetWalkableAt(keyValue.Key, false);
            }
        }

        public override BaseGrid Clone()
        {
            DynamicGrid tNewGrid = new DynamicGrid(null);

            foreach (KeyValuePair<GridPos, Node> keyValue in m_nodes)
            {
                tNewGrid.SetWalkableAt(keyValue.Key.x, keyValue.Key.y, true);

            }

            return tNewGrid;
        }
    }
    public abstract class BaseGrid
    {

        public BaseGrid()
        {
        }

        protected GridRect m_gridRect;
        public GridRect gridRect
        {
            get { return m_gridRect; }
        }

        public abstract int width { get; protected set; }

        public abstract int height { get; protected set; }

        public abstract Node GetNodeAt(int iX, int iY);

        public abstract bool IsWalkableAt(int iX, int iY);

        public abstract bool SetWalkableAt(int iX, int iY, bool iWalkable);

        public abstract Node GetNodeAt(GridPos iPos);

        public abstract bool IsWalkableAt(GridPos iPos);

        public abstract bool SetWalkableAt(GridPos iPos, bool iWalkable);

        public List<Node> GetNeighbors(Node iNode, bool iCrossCorners, bool iCrossAdjacentPoint)
        {
            int tX = iNode.x;
            int tY = iNode.y;
            List<Node> neighbors = new List<Node>();
            bool tS0 = false, tD0 = false,
                tS1 = false, tD1 = false,
                tS2 = false, tD2 = false,
                tS3 = false, tD3 = false;

            GridPos pos = new GridPos();
            if (this.IsWalkableAt(pos.Set(tX, tY - 1)))
            {
                neighbors.Add(GetNodeAt(pos));
                tS0 = true;
            }
            if (this.IsWalkableAt(pos.Set(tX + 1, tY)))
            {
                neighbors.Add(GetNodeAt(pos));
                tS1 = true;
            }
            if (this.IsWalkableAt(pos.Set(tX, tY + 1)))
            {
                neighbors.Add(GetNodeAt(pos));
                tS2 = true;
            }
            if (this.IsWalkableAt(pos.Set(tX - 1, tY)))
            {
                neighbors.Add(GetNodeAt(pos));
                tS3 = true;
            }
            if (iCrossCorners && iCrossAdjacentPoint)
            {
                tD0 = true;
                tD1 = true;
                tD2 = true;
                tD3 = true;
            }
            else if (iCrossCorners)
            {
                tD0 = tS3 || tS0;
                tD1 = tS0 || tS1;
                tD2 = tS1 || tS2;
                tD3 = tS2 || tS3;
            }
            else
            {
                tD0 = tS3 && tS0;
                tD1 = tS0 && tS1;
                tD2 = tS1 && tS2;
                tD3 = tS2 && tS3;
            }

            if (tD0 && this.IsWalkableAt(pos.Set(tX - 1, tY - 1)))
            {
                neighbors.Add(GetNodeAt(pos));
            }
            if (tD1 && this.IsWalkableAt(pos.Set(tX + 1, tY - 1)))
            {
                neighbors.Add(GetNodeAt(pos));
            }
            if (tD2 && this.IsWalkableAt(pos.Set(tX + 1, tY + 1)))
            {
                neighbors.Add(GetNodeAt(pos));
            }
            if (tD3 && this.IsWalkableAt(pos.Set(tX - 1, tY + 1)))
            {
                neighbors.Add(GetNodeAt(pos));
            }
            return neighbors;
        }

        public abstract void Reset();

        public abstract BaseGrid Clone();

    }
    public struct GridRect
    {
        public int minX;
        public int minY;
        public int maxX;
        public int maxY;

        public GridRect(int iMinX, int iMinY, int iMaxX, int iMaxY)
        {
            minX = iMinX;
            minY = iMinY;
            maxX = iMaxX;
            maxY = iMaxY;
        }

        public override int GetHashCode()
        {
            return minX ^ minY ^ maxX ^ maxY;
        }

        public override bool Equals(System.Object obj)
        {
            if (!(obj is GridRect))
                return false;
            GridRect p = (GridRect)obj;
            // Return true if the fields match:
            return (minX == p.minX) && (minY == p.minY) && (maxX == p.maxX) && (maxY == p.maxY);
        }

        public bool Equals(GridRect p)
        {
            // Return true if the fields match:
            return (minX == p.minX) && (minY == p.minY) && (maxX == p.maxX) && (maxY == p.maxY);
        }

        public static bool operator ==(GridRect a, GridRect b)
        {
            // If both are null, or both are same instance, return true.
            if (System.Object.ReferenceEquals(a, b))
            {
                return true;
            }

            // Return true if the fields match:
            return (a.minX == b.minX) && (a.minY == b.minY) && (a.maxX == b.maxX) && (a.maxY == b.maxY);
        }

        public static bool operator !=(GridRect a, GridRect b)
        {
            return !(a == b);
        }

        public GridRect Set(int iMinX, int iMinY, int iMaxX, int iMaxY)
        {
            this.minX = iMinX;
            this.minY = iMinY;
            this.maxX = iMaxX;
            this.maxY = iMaxY;
            return this;
        }
    }
    public struct GridPos
    {
        public int x;
        public int y;
        public GridPos(int iX, int iY)
        {
            this.x = iX;
            this.y = iY;
        }

        public override int GetHashCode()
        {
            return x ^ y;
        }

        public override bool Equals(System.Object obj)
        {
            if (!(obj is GridPos))
                return false;
            GridPos p = (GridPos)obj;
            // Return true if the fields match:
            return (x == p.x) && (y == p.y);
        }

        public bool Equals(GridPos p)
        {
            // Return true if the fields match:
            return (x == p.x) && (y == p.y);
        }

        public static bool operator ==(GridPos a, GridPos b)
        {
            // If both are null, or both are same instance, return true.
            if (System.Object.ReferenceEquals(a, b))
            {
                return true;
            }

            // Return true if the fields match:
            return a.x == b.x && a.y == b.y;
        }

        public static bool operator !=(GridPos a, GridPos b)
        {
            return !(a == b);
        }

        public GridPos Set(int iX, int iY)
        {
            this.x = iX;
            this.y = iY;
            return this;
        }
    }
    #endregion PathFinder

}