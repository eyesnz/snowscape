﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Utils;
//using Microsoft.Xna.Framework;
using OpenTK;
using System.Threading;
using System.IO;
using System.IO.Compression;

namespace TerrainGeneration
{
    public class TerrainGen : ITerrainGen
    {
        const int NUMTHREADS = 3;
        const int FILEMAGIC = 0x54455230;

        #region Generation Parameters
        public float TerrainSlumpMaxHeightDifference { get; set; }
        public float TerrainSlumpMovementAmount { get; set; }
        public int TerrainSlumpSamplesPerFrame { get; set; }

        public float TerrainSlump2MaxHeightDifference { get; set; }
        public float TerrainSlump2MovementAmount { get; set; }
        public int TerrainSlump2SamplesPerFrame { get; set; }

        public float TerrainCollapseMaxHeightDifference { get; set; }
        public float TerrainCollapseMovementAmount { get; set; }
        public float TerrainCollapseLooseThreshold { get; set; }
        public int TerrainCollapseSamplesPerFrame { get; set; }

        public int WaterNumParticles { get; set; }
        public int WaterIterationsPerFrame { get; set; }
        
        public float WaterDepositWaterCollapseAmount { get; set; }
        public float WaterSpeedLowpassAmount { get; set; }
        public float WaterCarryingCapacitySpeedCoefficient { get; set; } // 10.8
        public float WaterCarryingCapacityLowpass { get; set; }
        public float WaterMaxCarryingCapacity { get; set; } // 1.0
        public float WaterProportionToDropOnOverCapacity { get; set; } // 0.8
        public float WaterErosionSpeedCoefficient { get; set; } // 1.0
        public float WaterErosionHardErosionFactor { get; set; }  // 0.3
        public float WaterErosionCollapseToAmount { get; set; } // 0.02f
        public float WaterErosionMinSpeed { get; set; }
        public int WaterParticleMaxAge { get; set; }
        public float WaterParticleMinCarryingToSurvive { get; set; }

        /// <summary>
        /// Amount we add to the "water height/density" component per frame, multiplied by crossdistance
        /// </summary>
        public float WaterAccumulatePerFrame { get; set; }  // 0.001 originally

        /// <summary>
        /// This adds a random vector of length up to 0.5 x this factor to the fall vector.
        /// This will ruin the behaviour if you make it too big.
        /// </summary>
        public float WaterTurbulence { get; set; }

        /// <summary>
        /// The momentum of the water particles. Practically this means the amount of the previous fall vector added to the current one.
        /// Influences the perceived scale of the terrain.
        /// </summary>
        public float WaterMomentumFactor { get; set; }

        #endregion

        public int Iterations { get; private set; }
        public long WaterIterations { get; private set; }



        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct Cell
        {
            /// <summary>
            /// Hard rock - eroded by wind and water
            /// </summary>
            public float Hard;
            /// <summary>
            /// Loose material/soil/dust - moved by wind and water
            /// </summary>
            public float Loose;
            /// <summary>
            /// Suspended material - indicates erosion
            /// </summary>
            public float Erosion;
            /// <summary>
            /// Non-height component indicating how much flowing water is over this tile.
            /// </summary>
            public float MovingWater;

            /// <summary>
            /// Amount of suspended material carried over this tile.
            /// </summary>
            public float Carrying;

            public float Height
            {
                get
                {
                    return Hard + Loose;
                }
            }

            public float WHeight
            {
                get
                {
                    return Hard + Loose + MovingWater;
                }
            }
        }

        public int Width { get; private set; }
        public int Height { get; private set; }

        public Cell[] Map { get; private set; }
        private float[] TempDiffMap;

        private List<WaterErosionParticle> WaterParticles = new List<WaterErosionParticle>();

        public TerrainGen(int width, int height)
        {
            this.Width = width;
            this.Height = height;
            this.Map = new Cell[this.Width * this.Height];
            this.TempDiffMap = new float[this.Width * this.Height];


            // init parameters

            // Slump loose slopes - general case
            this.TerrainSlumpMaxHeightDifference = 1.0f;  // 1.0
            this.TerrainSlumpMovementAmount = 0.01f;
            this.TerrainSlumpSamplesPerFrame = 5000;

            // Slump loose slopes - rare case
            this.TerrainSlump2MaxHeightDifference = 0.5f;
            this.TerrainSlump2MovementAmount = 0.02f;
            this.TerrainSlump2SamplesPerFrame = 0;// 2000;

            // Collapse hard material - rare - used to simulate rockfall in slot canyons and cliffs
            this.TerrainCollapseMaxHeightDifference = 3.0f;
            this.TerrainCollapseMovementAmount = 0.08f;
            this.TerrainCollapseLooseThreshold = 1f;
            this.TerrainCollapseSamplesPerFrame = 0;// 500;

            // Water erosion
            this.WaterNumParticles = 10000;  // 4000
            this.WaterIterationsPerFrame = 7;  // 20
            
            this.WaterDepositWaterCollapseAmount = 0.02f;  // 0.05
            this.WaterCarryingCapacitySpeedCoefficient = 5.0f;  // 10 3
            this.WaterMaxCarryingCapacity = 20.0f;  // 100 50
            this.WaterCarryingCapacityLowpass = 0.2f;
            this.WaterProportionToDropOnOverCapacity = 0.9f;  // 0.8
            this.WaterErosionSpeedCoefficient = 0.01f;  // 1
            this.WaterErosionHardErosionFactor = 0.1f;
            this.WaterErosionCollapseToAmount = 0.005f;
            this.WaterErosionMinSpeed = 0.01f;  // 0.01
            this.WaterAccumulatePerFrame = 0.05f; //0.005 0.002f;

            this.WaterSpeedLowpassAmount = 0.5f;  // 0.2 0.8 
            this.WaterMomentumFactor = 0.0f; // 0.005 0 0.05f;  
            this.WaterTurbulence = 0.0f; // 0  0.05f;

            this.WaterParticleMaxAge = 100;  //min age of particle before it can be recycled
            this.WaterParticleMinCarryingToSurvive = 0.01f;

            this.Iterations = 0;
            this.WaterIterations = 0;

            Random r = new Random();

            for (int i = 0; i < this.WaterNumParticles; i++)
            {
                this.WaterParticles.Add(new WaterErosionParticle(r.Next(this.Width), r.Next(this.Height)));
            }

        }

        /// <summary>
        /// Gets the cell index of x,y
        /// 
        /// Deals with wraparound
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns></returns>

        private Func<int, int, int> C = (x, y) => ((x + 1024) & 1023) + (((y + 1024) & 1023) << 10);
        private Func<int, int> CX = (i) => i & 1023;
        private Func<int, int> CY = (i) => (i >> 10) & 1023;

        //private int C(int x, int y)
        //{
        //    return ((x + 1024) & 1023) + (((y + 1024) & 1023) << 10);
        //    //return x.Wrap(this.Width) + y.Wrap(this.Height) * this.Width;
        //}


        //private int CX(int i)
        //{
        //    return i & 1023;
        //    //return (i % this.Width).Wrap(this.Width);
        //}
        //private int CY(int i)
        //{
        //    return (i >> 10) & 1023;
        //    //return (i / this.Width).Wrap(this.Height);
        //}

        private void ClearTempDiffMap()
        {
            Array.Clear(this.TempDiffMap, 0, this.Width * this.Height);
        }

        public void InitTerrain1()
        {
            this.Clear(0.0f);

            this.AddSimplexNoise(6, 0.1f / (float)this.Width, 200.0f, h => h, h => h + h * h);
            this.AddSimplexNoise(12, 0.7f / (float)this.Width, 500.0f, h => Math.Abs(h), h => h + h * h);

            //this.AddSimplexNoise(5, 7.3f / (float)this.Width, 600.0f, h => Math.Abs(h), h => h * h);


            //this.AddSimplexNoise(8, 2.43f / (float)this.Width, 700.0f, h => Math.Abs(h), h => h * h);

            //this.AddSimplexNoise(10, 7.7f / (float)this.Width, 50.0f, h => Math.Abs(h), h => (h * h * 2f).Clamp(0.1f, 10.0f) - 0.1f);
            //this.AddSimplexNoise(5, 37.7f / (float)this.Width, 5.0f, h => Math.Abs(h), h => (h * h * 2f).Clamp(0.1f, 10.0f) - 0.1f);

            //this.AddSimplexNoise(3, 0.3f / (float)this.Width, 1300.0f, h => h, h => h);
            /*
            this.AddSimplexNoise(7, 1.3f / (float)this.Width, 800.0f, h => (h >= 0f ? h : -h), h => h * h);

            this.AddMultipliedSimplexNoise(
                3, 1.0f / (float)this.Width, h => h, 0.6f, 0.5f,
                10, 1.7f / (float)this.Width, h => (h >= 0f ? h : -h), 0.1f, 1.0f,
                h => h, 500.0f);
            */

            //this.AddSimplexNoise(5, 3.3f / (float)this.Width, 50.0f);
            this.AddLooseMaterial(30.0f);
            //this.AddSimplexNoiseToLoose(5, 17.7f / (float)this.Width, 5.0f);



            this.SetBaseLevel();
        }

        public void InitTerrain2()
        {
            this.Clear(0.0f);
            this.AddSimplexNoise(8, 0.37f / (float)this.Width, 100.0f);
            this.AddLooseMaterial(30.0f);
            this.SetBaseLevel();
        }




        public void ModifyTerrain()
        {
            if (this.Iterations % 16 == 0)
            {
                this.SortWater();
            }


            this.RunWater2(this.WaterIterationsPerFrame);
            //this.RunWater3(this.WaterIterationsPerFrame);

            this.Slump(this.TerrainSlumpMaxHeightDifference, this.TerrainSlumpMovementAmount, this.TerrainSlumpSamplesPerFrame);
            this.Slump(this.TerrainSlump2MaxHeightDifference, this.TerrainSlump2MovementAmount, this.TerrainSlump2SamplesPerFrame);
            this.Collapse(this.TerrainCollapseMaxHeightDifference, this.TerrainCollapseMovementAmount, 1f, this.TerrainCollapseSamplesPerFrame);

            // fade water amount
            // 0.96
            DecayWater(0.9f, 0.5f, 0.95f);

            this.Iterations++;
        }

        private void DecayWater(float MovingWaterDecay, float WaterErosionDecay, float CarryingDecay)
        {

            Parallel.For(0, this.Height, y =>
            {
                int i = y * this.Width;
                for (int x = 0; x < this.Width; x++)
                {
                    this.Map[i].MovingWater *= MovingWaterDecay;
                    this.Map[i].Erosion *= WaterErosionDecay;
                    this.Map[i].Carrying *= CarryingDecay;
                    i++;
                }
            });


        }


        public void Clear(float height)
        {
            for (int i = 0; i < Width * Height; i++)
            {
                this.Map[i] = new Cell();
            }
        }

        public void SetBaseLevel()
        {
            float min = this.Map.Select(c => c.Hard).Min();

            for (int i = 0; i < Width * Height; i++)
            {
                this.Map[i].Hard -= min;
            }
        }

        public void AddLooseMaterial(float amount)
        {
            for (int i = 0; i < Width * Height; i++)
            {
                this.Map[i].Loose += amount;
            }
        }


        #region Water


        public void SortWater()
        {
            this.WaterParticles.Sort(delegate(WaterErosionParticle a, WaterErosionParticle b) { return a.Pos.Y.CompareTo(b.Pos.Y) * this.Width + a.Pos.X.CompareTo(b.Pos.X); });
        }




        public void RunWater2(int CellsPerRun)
        {
            // This will probably be single-thread only due to extensive modification of the heightmap.
            // Could interlock the fucker, but that'll be a performance nightmare with tight loops.

            var up = new Vector3(0f, 0f, 1f);
            var rand = new Random();
            var tileDir = new Vector2(0f, 0f);
            var turbulence = new Vector3(0f, 0f, 0f);

            Func<int, float, float> LowestNeighbour = (i, h) => this.Map[i].WHeight < h ? this.Map[i].WHeight : h;

            foreach (var wp in this.WaterParticles)
            {
                int cellx = wp.Pos.CellX(this.Width);
                int celly = wp.Pos.CellY(this.Height);
                int celli = C(cellx, celly);
                int cellnx, cellny;
                int cellox = cellx, celloy = celly; // check for oscillation in a small area
                bool needReset = false;

                // run the particle for a number of cells
                for (int i = 0; i < CellsPerRun && !needReset; i++)
                {
                    wp.Age++;
                    this.Map[celli].Erosion += 100.0f;

                    this.WaterIterations++;

                    this.Map[celli].Carrying = this.Map[celli].Carrying * 0.5f + 0.5f * wp.CarryingAmount;  // vis for carrying amount

                    // get our current height
                    float h = this.Map[celli].WHeight;

                    #region Hole Check
                    // hole check - if the minimum height of our neighbours exceeds our own height, try to fill the hole
                    float lowestNeighbour = this.Map[C(cellx - 1, celly)].WHeight;
                    lowestNeighbour = LowestNeighbour(C(cellx + 1, celly), lowestNeighbour);
                    lowestNeighbour = LowestNeighbour(C(cellx, celly - 1), lowestNeighbour);
                    lowestNeighbour = LowestNeighbour(C(cellx, celly + 1), lowestNeighbour);
                    lowestNeighbour = LowestNeighbour(C(cellx - 1, celly - 1), lowestNeighbour);
                    lowestNeighbour = LowestNeighbour(C(cellx - 1, celly + 1), lowestNeighbour);
                    lowestNeighbour = LowestNeighbour(C(cellx + 1, celly - 1), lowestNeighbour);
                    lowestNeighbour = LowestNeighbour(C(cellx + 1, celly + 1), lowestNeighbour);

                    float ndiff = lowestNeighbour - h;
                    if (lowestNeighbour > h)
                    {
                        ndiff *= 1.001f;
                        if (wp.CarryingAmount > ndiff)
                        {
                            // carrying more than difference -> fill hole plus a little bit extra to make sure we can get out.
                            this.Map[celli].Loose += ndiff;
                            wp.CarryingAmount -= ndiff;

                            // adjust our height up accordingly
                            h += ndiff;
                        }
                        else
                        {
                            // stuck in a hole that we can't fill, reset
                            needReset = true;
                            break;
                        }
                    }
                    #endregion


                    // compute fall vector of current cell
                    var fall = FallVector(cellx, celly);

                    // if fall vector points up, bail out (should not happen given we've just filled the hole, or reset
                    if (fall.Z > 0.0f)
                    {
                        needReset = true;
                        break;
                    }

                    turbulence.X = (float)rand.NextDouble() - 0.5f;
                    turbulence.Y = (float)rand.NextDouble() - 0.5f;

                    wp.Vel = wp.Vel * this.WaterMomentumFactor + fall + turbulence * this.WaterTurbulence;
                    wp.Vel.Normalize();

                    // compute exit point and new cell coords
                    tileDir.X = wp.Vel.X;
                    tileDir.Y = wp.Vel.Y;

                    #region Edge Check
                    // sanity check: If the direction is changing such that we're going to get stuck on an edge, move point out into tile
                    if (tileDir.X < 0f)
                    {
                        if ((wp.Pos.X - (float)Math.Floor(wp.Pos.X)) < 0.05f)
                        {
                            wp.Pos.X += 0.05f;
                        }
                    }
                    else
                    {
                        if ((wp.Pos.X - (float)Math.Floor(wp.Pos.X)) > 0.95f)
                        {
                            wp.Pos.X -= 0.05f;
                        }
                    }
                    if (tileDir.Y < 0f)
                    {
                        if ((wp.Pos.Y - (float)Math.Floor(wp.Pos.Y)) < 0.05f)
                        {
                            wp.Pos.Y += 0.05f;
                        }
                    }
                    else
                    {
                        if ((wp.Pos.Y - (float)Math.Floor(wp.Pos.Y)) > 0.95f)
                        {
                            wp.Pos.Y -= 0.05f;
                        }
                    }
                    #endregion

                    // compute exit
                    var newPos = TileMath.TileIntersect(wp.Pos, tileDir, cellx, celly, out cellnx, out cellny);

                    // if the intersection func has returned the same cell, we're stuck and need to reset
                    if (cellx == cellnx && celly == cellny)
                    {
                        needReset = true;
                        break;
                    }

                    // calculate index of next cell
                    int cellni = C(cellnx, cellny);
                    float nh = this.Map[cellni].WHeight;

                    ndiff = nh - h;
                    // check to see if we're being forced uphill. If we are we drop material to try and level with our new position. If we can't do that we reset.


                    // calculate distance that we're travelling across cell.
                    float crossdistance = (newPos - wp.Pos).Length;

                    if (ndiff > 0f)
                    {
                        // we are moving uphill... shouldn't happen, but try to remedy
                        float amountToDrop = ndiff * 1.02f;

                        if (wp.CarryingAmount > amountToDrop)
                        {
                            // carrying more than difference -> fill hole
                            this.Map[celli].Loose += amountToDrop;
                            wp.CarryingAmount -= amountToDrop;
                            h += amountToDrop;
                            ndiff = nh - h;
                        }
                        else
                        {
                            // stuck in hole, reset
                            needReset = true;
                            break;
                        }
                    }

                    // now we should really be moving downhill
                    if (ndiff < 0f)
                    {
                        ndiff = -ndiff;

                        //float slope = (float)Math.Atan(-ndiff) / 1.570796f;
                        float slopeLength = ((float)Math.Sqrt(ndiff * ndiff + crossdistance * crossdistance));  // drop over total distance travelled

                        // calculate accelleration due to gravity
                        float accel = 2.0f * (ndiff / slopeLength);

                        float newSpeed = wp.Speed + accel * 0.5f;

                        wp.Speed *= 0.95f; // drag
                        wp.Speed = wp.Speed * this.WaterSpeedLowpassAmount + (1.0f - this.WaterSpeedLowpassAmount) * newSpeed;

                        // modify speed of particle
                        //wp.Speed = wp.Speed * this.WaterSpeedLowpassAmount + (1.0f - this.WaterSpeedLowpassAmount) * slope;

                        // blend speed into map for display
                        //this.Map[celli].Erosion = this.Map[celli].Erosion * 0.5f + 0.5f * wp.Speed;
                    }


                    // work out fraction of cell we're crossing, as a proportion of the length of the diagonal (root-2)
                    crossdistance /= 1.4142136f;

                    // add some moving water so we can see it.
                    this.Map[celli].MovingWater += WaterAccumulatePerFrame * crossdistance;

                    // calculate new carrying capacity
                    float newCarryingCapacity = (this.WaterCarryingCapacitySpeedCoefficient * wp.Speed);
                    wp.CarryingCapacity = wp.CarryingCapacity * this.WaterCarryingCapacityLowpass + (1f - this.WaterCarryingCapacityLowpass) * newCarryingCapacity;
                    if (wp.CarryingCapacity > this.WaterMaxCarryingCapacity)
                    {
                        wp.CarryingCapacity = this.WaterMaxCarryingCapacity;
                    }

                    // if we're over our carrying capacity, start dropping material
                    float cdiff = wp.CarryingAmount - wp.CarryingCapacity;
                    if (cdiff > 0.0f)
                    {
                        cdiff *= this.WaterProportionToDropOnOverCapacity * crossdistance; // amount to drop

                        // drop a portion of our material
                        this.Map[celli].Loose += cdiff;  // drop at old location
                        wp.CarryingAmount -= cdiff;

                        CollapseFrom(cellx, celly, this.WaterDepositWaterCollapseAmount);
                    }
                    else  // we're under our carrying capacity, so do some erosion
                    {
                        cdiff = -cdiff;

                        float loose = this.Map[celli].Loose;
                        float hard = this.Map[celli].Hard;


                        if (wp.Speed > this.WaterErosionMinSpeed)
                        {
                            // erosion rate goes up with the square of water velocity over the critical velocity (shear stress)
                            float erosionFactor = (wp.Speed - this.WaterErosionMinSpeed);
                            erosionFactor *= erosionFactor;
                            erosionFactor *= crossdistance * this.WaterErosionSpeedCoefficient;


                            // we can only erode the difference between our height and our lowest neighbour.
                            //erosionFactor = erosionFactor.ClampInclusive(0f, (h - lowestNeighbour) * 3.0f);
                            //if (erosionFactor > (h - lowestNeighbour))
                            //{
                            //    erosionFactor = h - lowestNeighbour;
                            //}

                            float looseErodeAmount = erosionFactor; // erosion coefficient for loose material

                            if (looseErodeAmount > cdiff)
                            {
                                looseErodeAmount = cdiff;
                            }

                            // first of all, see if we can pick up any loose material.
                            if (loose > 0.0f)
                            {
                                if (looseErodeAmount > loose)
                                {
                                    looseErodeAmount = loose;
                                }

                                this.Map[celli].Loose -= looseErodeAmount;
                                wp.CarryingAmount += looseErodeAmount;

                                cdiff -= looseErodeAmount;
                            }

                            // if we've got any erosion potential left, use it
                            float hardErodeAmount = (erosionFactor - looseErodeAmount) * this.WaterErosionHardErosionFactor;
                            if (hardErodeAmount > cdiff)
                            {
                                hardErodeAmount = cdiff;
                            }

                            if (hardErodeAmount > 0.0f)
                            {
                                this.Map[celli].Hard -= hardErodeAmount;
                                wp.CarryingAmount += hardErodeAmount; // loose material is less dense than hard, so make it greater.
                            }

                            //CollapseTo(cellx, celly, this.WaterErosionCollapseToAmount);
                            //CollapseFrom(cellx + 1, celly, this.WaterDepositWaterCollapseAmount);
                            //CollapseFrom(cellx - 1, celly, this.WaterDepositWaterCollapseAmount);
                            //CollapseFrom(cellx, celly - 1, this.WaterDepositWaterCollapseAmount);
                            //CollapseFrom(cellx, celly + 1, this.WaterDepositWaterCollapseAmount);
                        }
                    }
                    //}

                    // collapse material toward current cell
                    CollapseTo(cellx, celly, this.WaterErosionCollapseToAmount);

                    // if we're old and not carrying much at all, reset
                    if (wp.Age > this.WaterParticleMaxAge && wp.CarryingAmount < this.WaterParticleMinCarryingToSurvive)
                    {
                        needReset = true;
                        break;
                    }

                    // move particle params
                    wp.Pos = newPos;
                    cellx = cellnx; // this may not work across loop runs. May need to store on particle.
                    celly = cellny;
                    celli = cellni;
                }


                if (needReset)
                {
                    this.Map[celli].Loose += wp.CarryingAmount;
                    CollapseFrom(cellx, celly, 0.1f);

                    wp.Reset(rand.Next(this.Width), rand.Next(this.Height), rand);// reset particle
                }

            }

        }

        void DistributeRemainingMaterial(float amount, int x, int y)
        {
            float distAmount = amount / 10.0f;
            float totalDist = 0f;
            float h = this.Map[C(x, y)].Height;
            float threshold = h + 0.01f;

            // if a neighbour is within threshold of our height, give it some material.
            Func<int, int, float> Distribute = (dx, dy) =>
            {
                int i = C(x + dx, y + dy);
                if (this.Map[i].Height < threshold)
                {
                    this.Map[i].Loose += distAmount;
                    return distAmount;
                }
                return 0f;
            };

            totalDist += Distribute(-1, -1);
            totalDist += Distribute(0, -1);
            totalDist += Distribute(1, -1);
            totalDist += Distribute(-1, 0);
            totalDist += Distribute(1, 0);
            totalDist += Distribute(-1, 1);
            totalDist += Distribute(0, 1);
            totalDist += Distribute(1, 1);

            this.Map[C(x, y)].Loose += (amount - totalDist);
        }


        #endregion

        #region noise
        public void AddSimplexNoise(int octaves, float scale, float amplitude)
        {
            var r = new Random();

            float rx = (float)r.NextDouble();
            float ry = (float)r.NextDouble();


            Parallel.For(0, this.Height,
                (y) =>
                {
                    int i = (int)y * this.Width;
                    float s = (float)y / (float)this.Height;
                    for (int x = 0; x < this.Width; x++)
                    {
                        float h = 0.0f;
                        float t = (float)x / (float)this.Width;
                        for (int j = 1; j <= octaves; j++)
                        {
                            //h += SimplexNoise.noise((float)rx + x * scale * (1 << j), (float)ry + y * scale * (1 << j), j * 3.3f) * (amplitude / ((1 << j) + 1));
                            h += SimplexNoise.wrapnoise(s, t, (float)this.Width, (float)this.Height, rx, ry, (float)(scale * (1 << j))) * (float)(1.0 / ((1 << j) + 1));
                        }
                        this.Map[i].Hard += h * amplitude;
                        i++;
                    }
                }
            );
        }
        public void AddSimplexNoiseToLoose(int octaves, float scale, float amplitude)
        {
            var r = new Random();

            float rx = (float)r.NextDouble();
            float ry = (float)r.NextDouble();


            Parallel.For(0, this.Height,
                (y) =>
                {
                    int i = (int)y * this.Width;
                    float s = (float)y / (float)this.Height;
                    for (int x = 0; x < this.Width; x++)
                    {
                        float h = 0.0f;
                        float t = (float)x / (float)this.Width;
                        for (int j = 1; j <= octaves; j++)
                        {
                            //h += SimplexNoise.noise((float)rx + x * scale * (1 << j), (float)ry + y * scale * (1 << j), j * 3.3f) * (amplitude / ((1 << j) + 1));
                            h += SimplexNoise.wrapnoise(s, t, (float)this.Width, (float)this.Height, rx, ry, (float)(scale * (1 << j))) * (float)(1.0 / ((1 << j) + 1));
                        }
                        this.Map[i].Loose += h * amplitude;
                        i++;
                    }
                }
            );
        }

        public void AddSimplexNoise(int octaves, float scale, float amplitude, Func<float, float> transform, Func<float, float> postTransform)
        {
            var r = new Random();

            float rx = (float)r.NextDouble();
            float ry = (float)r.NextDouble();


            Parallel.For(0, this.Height,
                (y) =>
                {
                    int i = (int)y * this.Width;
                    float s = (float)y / (float)this.Height;
                    for (int x = 0; x < this.Width; x++)
                    {
                        float h = 0.0f;
                        float t = (float)x / (float)this.Width;
                        for (int j = 1; j <= octaves; j++)
                        {
                            h += transform(SimplexNoise.wrapnoise(s, t, (float)this.Width, (float)this.Height, rx, ry, (float)(scale * (1 << j))) * (float)(1.0 / ((1 << j) + 1)));
                        }

                        if (postTransform != null)
                        {
                            this.Map[i].Hard += postTransform(h) * amplitude;
                        }
                        else
                        {
                            this.Map[i].Hard += h * amplitude;
                        }
                        i++;
                    }
                }
            );
        }

        public void AddSimplexPowNoise(int octaves, float scale, float amplitude, float power, Func<float, float> postTransform)
        {
            var r = new Random();

            float rx = (float)r.NextDouble();
            float ry = (float)r.NextDouble();


            Parallel.For(0, this.Height,
                (y) =>
                {
                    int i = (int)y * this.Width;
                    float s = (float)y / (float)this.Height;
                    for (int x = 0; x < this.Width; x++)
                    {
                        float h = 0.0f;
                        float t = (float)x / (float)this.Width;
                        for (int j = 1; j <= octaves; j++)
                        {
                            //h += SimplexNoise.noise((float)rx + x * scale * (1 << j), (float)ry + y * scale * (1 << j), j * 3.3f) * (amplitude / ((1 << j) + 1));
                            h += SimplexNoise.wrapnoise(s, t, (float)this.Width, (float)this.Height, rx, ry, (float)(scale * (1 << j))) * (float)(1.0 / ((1 << j) + 1));
                        }
                        this.Map[i].Hard += postTransform((float)Math.Pow(h, power) * amplitude);
                        i++;
                    }
                }
            );
        }

        /// <summary>
        /// One set of noise multiplied by another.
        /// </summary>
        /// <param name="octaves"></param>
        /// <param name="scale"></param>
        /// <param name="amplitude"></param>
        /// <param name="transform"></param>
        /// <param name="postTransform"></param>
        public void AddMultipliedSimplexNoise(
            int octaves1, float scale1, Func<float, float> transform1, float offset1, float mul1,
            int octaves2, float scale2, Func<float, float> transform2, float offset2, float mul2,
            Func<float, float> postTransform,
            float amplitude)
        {
            var r = new Random();

            float rx = (float)r.NextDouble();
            float ry = (float)r.NextDouble();


            Parallel.For(0, this.Height,
                (y) =>
                {
                    int i = (int)y * this.Width;
                    float s = (float)y / (float)this.Height;
                    for (int x = 0; x < this.Width; x++)
                    {
                        float t = (float)x / (float)this.Width;

                        float h = 0.0f;

                        for (int j1 = 1; j1 <= octaves1; j1++)
                        {
                            h += transform1(SimplexNoise.wrapnoise(s, t, (float)this.Width, (float)this.Height, rx, ry, (float)(scale1 * (1 << j1))) * (float)(1.0 / ((1 << j1) + 1)));
                        }

                        h = h * mul1 + offset1;

                        float h2 = 0f;

                        for (int j2 = 1; j2 <= octaves2; j2++)
                        {
                            h2 += transform2(SimplexNoise.wrapnoise(s, t, (float)this.Width, (float)this.Height, rx, ry, (float)(scale2 * (1 << j2))) * (float)(1.0 / ((1 << j2) + 1)));
                        }

                        h2 = h2 * mul2 + offset2;

                        h *= h2;


                        if (postTransform != null)
                        {
                            this.Map[i].Hard += postTransform(h) * amplitude;
                        }
                        else
                        {
                            this.Map[i].Hard += h * amplitude;
                        }
                        i++;
                    }
                }
            );
        }


        public void AddDiscontinuousNoise(int octaves, float scale, float amplitude, float threshold)
        {
            var r = new Random(1);

            double rx = r.NextDouble();
            double ry = r.NextDouble();

            Parallel.For(0, this.Height,
                (y) =>
                {
                    int i = y * this.Width;
                    for (int x = 0; x < this.Width; x++)
                    {
                        //this.Map[i].Hard += SimplexNoise.noise((float)rx + x * scale * (1 << j), (float)ry + y * scale * (1 << j), j * 3.3f) * (amplitude / ((1 << j) + 1));

                        float a = 0.0f;
                        for (int j = 1; j < octaves; j++)
                        {
                            a += SimplexNoise.noise((float)rx + x * scale * (1 << j), (float)ry + y * scale * (1 << j), j * 3.3f) * (amplitude / ((1 << j) + 1));
                        }

                        if (a > threshold)
                        {
                            this.Map[i].Hard += amplitude;
                        }

                        i++;
                    }
                }
            );
        }
        #endregion


        /// <summary>
        /// Slumps the terrain by looking at difference between cell and adjacent cells
        /// </summary>
        /// <param name="threshold">height difference that will trigger a redistribution of material</param>
        /// <param name="amount">amount of material to move (proportional to difference)</param>
        /// 
        public void Slump(float _threshold, float amount, int numIterations)
        {
            //float amount2 = amount * 0.707f;
            float _threshold2 = (float)(_threshold * Math.Sqrt(2.0));
            this.ClearTempDiffMap();

            Func<int, int, float, float, float, float[], float> SlumpF = (pFrom, pTo, h, a, threshold, diffmap) =>
            {
                float loose = this.Map[pFrom].Loose; // can only slump loose material.
                if (loose > 0.0f)
                {
                    float diff = (this.Map[pFrom].Hard + loose) - h;
                    if (diff > threshold)
                    {
                        diff -= threshold;
                        if (diff > loose)
                        {
                            diff = loose;
                        }

                        diff *= a;

                        diffmap[pFrom] -= diff;
                        diffmap[pTo] += diff;

                        //this.Map[pFrom].Erosion += diff;

                        return diff;
                    }
                }
                return 0f;
            };

            Action<int, int, float[]> SlumpTo = (x, y, diffmap) =>
            {
                int p = C(x, y);
                int n = C(x, y - 1);
                int s = C(x, y + 1);
                int w = C(x - 1, y);
                int e = C(x + 1, y);

                int nw = C(x - 1, y - 1);
                int ne = C(x + 1, y - 1);
                int sw = C(x - 1, y + 1);
                int se = C(x + 1, y + 1);

                float h = this.Map[p].Hard + this.Map[p].Loose;
                float a = amount; // (amount * (this.Map[p].MovingWater * 50.0f + 0.2f)).Clamp(0.005f, 0.1f);  // slump more where there is more water

                float th = _threshold;// / (1f + this.Map[p].MovingWater * 200f);
                float th2 = th * 1.414f;

                h += SlumpF(n, p, h, a, th, diffmap);
                h += SlumpF(s, p, h, a, th, diffmap);
                h += SlumpF(w, p, h, a, th, diffmap);
                h += SlumpF(e, p, h, a, th, diffmap);

                h += SlumpF(nw, p, h, a, th2, diffmap);
                h += SlumpF(ne, p, h, a, th2, diffmap);
                h += SlumpF(sw, p, h, a, th2, diffmap);
                h += SlumpF(se, p, h, a, th2, diffmap);
            };

            //var threadlocal = new { diffmap = new float[this.Width * this.Height], r = new Random() };
            var threadlocal = new { diffmap = this.TempDiffMap, r = new Random() };
            for (int i = 0; i < numIterations; i++)
            {
                int x = threadlocal.r.Next(this.Width);
                int y = threadlocal.r.Next(this.Height);

                SlumpTo(x, y, threadlocal.diffmap);
            }

            ParallelHelper.For2D(this.Width, this.Height, (i) =>
            {
                this.Map[i].Loose += threadlocal.diffmap[i];
            });


            //Parallel.For(0, 8, j =>
            //{
            //    int ii = j * ((this.Width * this.Height) >> 3);
            //    for (int i = 0; i < (this.Width * this.Height) >> 3; i++)
            //    {
            //        this.Map[ii].Loose += threadlocal.diffmap[ii];
            //        ii++;
            //    }
            //});


        }


        /// <summary>
        /// Similar to Slump(), but works on hard material instead of loose, and only when amount of loose coverage is below a certain threshold
        /// </summary>
        /// <param name="_threshold"></param>
        /// <param name="amount"></param>
        /// <param name="numIterations"></param>
        public void Collapse(float _threshold, float amount, float looseThreshold, int numIterations)
        {
            //float amount2 = amount * 0.707f;
            float _threshold2 = (float)(_threshold * Math.Sqrt(2.0));
            this.ClearTempDiffMap();

            Func<int, int, float, float, float, float[], float> SlumpF = (pFrom, pTo, h, a, threshold, diffmap) =>
            {

                if (this.Map[pFrom].Loose > looseThreshold)
                {
                    return 0f;
                }

                float diff = this.Map[pFrom].Hard - h;
                if (diff > threshold)
                {
                    diff -= threshold;

                    diff *= a;

                    diffmap[pFrom] -= diff;
                    diffmap[pTo] += diff;

                    //this.Map[pFrom].Erosion += diff;

                    return diff;
                }

                return 0f;
            };

            Action<int, int, float[]> SlumpTo = (x, y, diffmap) =>
            {
                int p = C(x, y);
                int n = C(x, y - 1);
                int s = C(x, y + 1);
                int w = C(x - 1, y);
                int e = C(x + 1, y);

                int nw = C(x - 1, y - 1);
                int ne = C(x + 1, y - 1);
                int sw = C(x - 1, y + 1);
                int se = C(x + 1, y + 1);

                float h = this.Map[p].Hard + this.Map[p].Loose;

                h += SlumpF(n, p, h, amount, _threshold, diffmap);
                h += SlumpF(s, p, h, amount, _threshold, diffmap);
                h += SlumpF(w, p, h, amount, _threshold, diffmap);
                h += SlumpF(e, p, h, amount, _threshold, diffmap);

                h += SlumpF(nw, p, h, amount, _threshold2, diffmap);
                h += SlumpF(ne, p, h, amount, _threshold2, diffmap);
                h += SlumpF(sw, p, h, amount, _threshold2, diffmap);
                h += SlumpF(se, p, h, amount, _threshold2, diffmap);
            };

            //var threadlocal = new { diffmap = new float[this.Width * this.Height], r = new Random() };
            var threadlocal = new { diffmap = this.TempDiffMap, r = new Random() };
            for (int i = 0; i < numIterations; i++)
            {
                int x = threadlocal.r.Next(this.Width);
                int y = threadlocal.r.Next(this.Height);

                SlumpTo(x, y, threadlocal.diffmap);
            }

            Parallel.For(0, 8, j =>
            {
                int ii = j * ((this.Width * this.Height) >> 3);
                for (int i = 0; i < (this.Width * this.Height) >> 3; i++)
                {
                    float d = threadlocal.diffmap[ii];
                    if (d < 0)
                    {
                        this.Map[ii].Hard += d;
                    }
                    else
                    {
                        this.Map[ii].Loose += d;
                    }
                    ii++;
                }
            });


        }

        private Func<Cell[], int, int, float, float, float> CollapseCellFunc = (m, collapseFromCell, collapseToCell, collapseFromHeight, a) =>
        {
            float diff = (collapseFromHeight - m[collapseToCell].Height);


            if (diff > 0f)
            {
                diff = Utils.Utils.Min(diff, m[collapseFromCell].Loose * 0.15f) * a;
                m[collapseToCell].Loose += diff;
                return diff;
            }
            return 0f;
        };

        private Func<Cell[], int, int, float, float, float> CollapseToCellFunc = (m, collapseToCell, collapseFromCell, collapseToHeight, a) =>
        {
            float diff = (m[collapseFromCell].Height - collapseToHeight);

            if (diff > 0f)
            {
                diff = Utils.Utils.Min(diff, m[collapseFromCell].Loose * 0.15f) * a;
                m[collapseFromCell].Loose -= diff;
                return diff;
            }
            return 0f;
        };

        /// <summary>
        /// Collapses loose material away from the specified point.
        /// 
        /// 
        /// </summary>
        /// <param name="cx"></param>
        /// <param name="cy"></param>
        /// <param name="amount"></param>
        public void CollapseFrom(int cx, int cy, float amount)
        {
            int ci = C(cx, cy);
            float h = this.Map[ci].Height;
            float dh = 0f;
            float amount2 = amount * 0.707f;

            dh += CollapseCellFunc(this.Map, ci, C(cx - 1, cy), h, amount);
            dh += CollapseCellFunc(this.Map, ci, C(cx + 1, cy), h, amount);
            dh += CollapseCellFunc(this.Map, ci, C(cx, cy - 1), h, amount);
            dh += CollapseCellFunc(this.Map, ci, C(cx, cy + 1), h, amount);
            dh += CollapseCellFunc(this.Map, ci, C(cx - 1, cy - 1), h, amount2);
            dh += CollapseCellFunc(this.Map, ci, C(cx - 1, cy + 1), h, amount2);
            dh += CollapseCellFunc(this.Map, ci, C(cx + 1, cy - 1), h, amount2);
            dh += CollapseCellFunc(this.Map, ci, C(cx + 1, cy + 1), h, amount2);

            if (dh < this.Map[ci].Loose)
            {
                this.Map[ci].Loose -= dh;
            }
            else
            {
                this.Map[ci].Hard -= (dh - this.Map[ci].Loose);
                this.Map[ci].Loose = 0f;
            }
        }

        public void CollapseTo(int cx, int cy, float amount)
        {
            int ci = C(cx, cy);
            float h = this.Map[ci].Height;
            float dh = 0f;
            float amount2 = amount * 0.707f;

            dh += CollapseToCellFunc(this.Map, ci, C(cx - 1, cy), h, amount);
            dh += CollapseToCellFunc(this.Map, ci, C(cx + 1, cy), h, amount);
            dh += CollapseToCellFunc(this.Map, ci, C(cx, cy - 1), h, amount);
            dh += CollapseToCellFunc(this.Map, ci, C(cx, cy + 1), h, amount);
            dh += CollapseToCellFunc(this.Map, ci, C(cx - 1, cy - 1), h, amount2);
            dh += CollapseToCellFunc(this.Map, ci, C(cx - 1, cy + 1), h, amount2);
            dh += CollapseToCellFunc(this.Map, ci, C(cx + 1, cy - 1), h, amount2);
            dh += CollapseToCellFunc(this.Map, ci, C(cx + 1, cy + 1), h, amount2);

            this.Map[ci].Loose += dh;

        }

        #region utils

        private Vector3 CellNormal(int cx, int cy)
        {
            float h1 = this.Map[C(cx, cy - 1)].Height;
            float h2 = this.Map[C(cx, cy + 1)].Height;
            float h3 = this.Map[C(cx - 1, cy)].Height;
            float h4 = this.Map[C(cx + 1, cy)].Height;

            return Vector3.Normalize(new Vector3(h3 - h4, h1 - h2, 2f));
        }

        // fall vector as the weighted sum of the vectors between cells
        private Vector3 FallVector(int cx, int cy)
        {
            //float diag = 1.0f;

            float h0 = this.Map[C(cx, cy)].WHeight;
            float h1 = this.Map[C(cx, cy - 1)].WHeight;
            float h2 = this.Map[C(cx, cy + 1)].WHeight;
            float h3 = this.Map[C(cx - 1, cy)].WHeight;
            float h4 = this.Map[C(cx + 1, cy)].WHeight;
            /*
            float h5 = this.Map[C(cx - 1, cy - 1)].WHeight;
            float h6 = this.Map[C(cx - 1, cy + 1)].WHeight;
            float h7 = this.Map[C(cx + 1, cy - 1)].WHeight;
            float h8 = this.Map[C(cx + 1, cy + 1)].WHeight;
            */
            Vector3 f = Vector3.Zero;

            /*
            f.Y -= h0 - h1;
            f.Z -= h0 - h1;

            f.Y += h0 - h2;
            f.Z -= h0 - h2;

            f.X -= h0 - h3;
            f.Z -= h0 - h3;

            f.X += h0 - h4;
            f.Z -= h0 - h4;
            */

            f += (new Vector3(0, -1, h1 - h0) * (h0 - h1));
            f += (new Vector3(0, 1, h2 - h0) * (h0 - h2));
            f += (new Vector3(-1, 0, h3 - h0) * (h0 - h3));
            f += (new Vector3(1, 0, h4 - h0) * (h0 - h4));

            /*
            f += (new Vector3(-1, -1, h5 - h0) * ((h0 - h5))) * diag;
            f += (new Vector3(-1, 1, h6 - h0) * ((h0 - h6))) * diag;
            f += (new Vector3(1, -1, h7 - h0) * ((h0 - h7))) * diag;
            f += (new Vector3(1, 1, h8 - h0) * ((h0 - h8))) * diag;*/

            f.Normalize();
            //f *= 0.25f;
            return f;
        }

        #endregion

        public float HeightAt(float x, float y)
        {
            int xx = (int)(x * this.Width);
            int yy = (int)(y * this.Height);

            float xfrac = (x * (float)Width) - (float)xx;
            float yfrac = (y * (float)Height) - (float)yy;

            float h00 = this.Map[C(xx, yy)].Height;
            float h10 = this.Map[C(xx + 1, yy)].Height;
            float h01 = this.Map[C(xx, yy + 1)].Height;
            float h11 = this.Map[C(xx + 1, yy + 1)].Height;


            //return MathHelper.Lerp(MathHelper.Lerp(h00, h10, xfrac), MathHelper.Lerp(h01, h11, xfrac), yfrac);
            return yfrac.Lerp(xfrac.Lerp(h00, h10), xfrac.Lerp(h01, h11));
        }

        public Vector3 ClampToGround(Vector3 pos)
        {
            pos.Y = this.HeightAt(pos.X, pos.Z) / 4096.0f;
            return pos;
        }


        #region File IO

        public void Save(string filename)
        {
            using (var fs = new FileStream(filename, FileMode.Create, FileAccess.Write, FileShare.None, 256 * 1024))
            {
                using (var sw = new BinaryWriter(fs))
                {
                    sw.Write(FILEMAGIC);
                    sw.Write(this.Width);
                    sw.Write(this.Height);

                    for (int i = 0; i < this.Width * this.Height; i++)
                    {
                        sw.Write(this.Map[i].Hard);
                        sw.Write(this.Map[i].Loose);
                        sw.Write(this.Map[i].Erosion);
                        sw.Write(this.Map[i].MovingWater);
                    }

                    sw.Close();
                }
                fs.Close();
            }
        }

        public void Load(string filename)
        {
            using (var fs = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.None, 256 * 1024))
            {
                using (var sr = new BinaryReader(fs))
                {
                    int magic = sr.ReadInt32();
                    if (magic != FILEMAGIC)
                    {
                        throw new Exception("Not a terrain file");
                    }

                    int w, h;

                    w = sr.ReadInt32();
                    h = sr.ReadInt32();

                    if (w != this.Width || h != this.Height)
                    {
                        // TODO: handle size changes
                        throw new Exception(string.Format("Terrain size {0}x{1} did not match generator size {2}x{3}", w, h, this.Width, this.Height));
                    }



                    for (int i = 0; i < this.Width * this.Height; i++)
                    {
                        this.Map[i].Hard = sr.ReadSingle();
                        this.Map[i].Loose = sr.ReadSingle();
                        this.Map[i].Erosion = sr.ReadSingle();
                        this.Map[i].MovingWater = sr.ReadSingle();

                        this.Map[i].MovingWater = 0f;
                        this.Map[i].Erosion = 0f;
                    }

                    sr.Close();
                }
                fs.Close();
            }

        }

        #endregion


    }
}
