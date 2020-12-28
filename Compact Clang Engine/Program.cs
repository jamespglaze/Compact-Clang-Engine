using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System;
using VRage.Collections;
using VRage.Game.Components;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Game;
using VRageMath;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        static readonly string engineTag = "Impulse Engine";
        static readonly float restDisplacement = -0.2f;

        static float maxDisplacement = -0.2f;
        static bool step = true;
        static bool doRunRefresh = true;
        bool enginesActive = true;

        IMyShipController mainControl = null;
        List<IMyShipController> controllers = new List<IMyShipController>();

        List<IMyBlockGroup> enginesRaw = new List<IMyBlockGroup>();
        List<Engine> engines = new List<Engine>();

        Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update1;
        }

        void Main(string argument)
        {
            switch (argument.ToUpper())
            {
                case "TOGGLE":
                    enginesActive = !enginesActive;
                    break;

                default:
                    break;
            }
            if (doRunRefresh)
            {
                RefreshBlocks();
                doRunRefresh = false;
            }
            //Input vector relative to grid
            Vector3D inputLocal = mainControl.MoveIndicator;
            //Normalized input vector relative to world
            Vector3D inputWorld = mainControl.WorldMatrix.Backward * inputLocal.Z + mainControl.WorldMatrix.Right * inputLocal.X + mainControl.WorldMatrix.Up * inputLocal.Y;
            if (inputWorld.LengthSquared() > 0)
                inputWorld = Vector3D.Normalize(inputWorld);
            //Ships velocity vector relative to world
            Vector3D shipVelocity = mainControl.GetShipVelocities().LinearVelocity;

            if (enginesActive)
                ApplyThrust(shipVelocity, inputWorld, mainControl.GetShipSpeed(), mainControl.CalculateShipMass().PhysicalMass);
        }

        void RefreshBlocks()
        {
            //Get an occupied ship controller to reference for orientation.
            GridTerminalSystem.GetBlocksOfType(controllers);
            foreach (IMyShipController control in controllers)
            {
                if (control.CanControlShip)
                {
                    mainControl = control;
                    if (control.IsUnderControl)
                        break;
                }
            }

            GridTerminalSystem.GetBlockGroups(enginesRaw, engine => engine.Name.Contains(engineTag));
            foreach (IMyBlockGroup engineRaw in enginesRaw)
                engines.Add(new Engine(engineRaw));
        }

        void ApplyThrust(Vector3D shipVelocity, Vector3D inputWorld, double shipSpeed, float mass)
        {
            foreach (Engine engine in engines)
            {
                try
                {
                    float scalePower = (engine.Drivers.Count * 1000000/ mass);
                    foreach (IMyMotorAdvancedStator driver in engine.Drivers)
                    {
                        //Which direction is this driver pointing relative to the world?
                        Vector3D eVector = driver.WorldMatrix.Up;
                        double eDotInput = eVector.Dot(inputWorld);
                        double eDotShipVelocity = eVector.Dot(shipVelocity);
                        float rotorDisplacement;

                        //Is this driver going to help accelerate in the intended direction?
                        if (eDotInput > 0.05)
                            engine.ExecuteDriver(driver, -maxDisplacement);

                        else if (-eDotInput > 0.05)
                            engine.ExecuteDriver(driver, maxDisplacement);

                        else
                        {
                            //Is this driver going to help dampers stop?
                            if (eDotInput == 0 && -eDotShipVelocity > 0.01)
                            {
                                rotorDisplacement = -eDotShipVelocity < scalePower ? (float)(-maxDisplacement * (-eDotShipVelocity / scalePower)) : -maxDisplacement;
                                engine.ExecuteDriver(driver, rotorDisplacement);
                            }
                            else if (eDotInput == 0 && eDotShipVelocity > 0.01)
                            {
                                rotorDisplacement = eDotShipVelocity < scalePower ? (float)(maxDisplacement * eDotShipVelocity / scalePower) : maxDisplacement;
                                engine.ExecuteDriver(driver, rotorDisplacement);
                            }
                            else
                                engine.ResetDriver(driver);
                        }
                        driver.Attach();
                    }
                    engine.ExecuteCargoShift();
                }
                catch
                {
                    doRunRefresh = true;
                    Me.CustomData = ("Encountered Error at " + DateTime.Now);
                }
            }
            step = !step;
            maxDisplacement = -maxDisplacement;
        }

        class Engine
        {
            public IMyBlockGroup EngineRaw { get; set; }
            public List<IMyMotorAdvancedStator> Drivers { get; set; } = new List<IMyMotorAdvancedStator>();
            public List<IMyCargoContainer> ContainerTempList { get; set; } = new List<IMyCargoContainer>();
            public List<IMyInventory> ContainerEnds { get; set; } = new List<IMyInventory>();
            public List<IMyInventory> ContainerBases { get; set; } = new List<IMyInventory>();

            public Engine(IMyBlockGroup engine)
            {
                EngineRaw = engine;
                PopulateEngine();
            }

            public void PopulateEngine()
            {
                EngineRaw.GetBlocksOfType(Drivers);

                EngineRaw.GetBlocksOfType(ContainerTempList, container => container.CustomName.Contains("End"));
                foreach (IMyCargoContainer ContainerTempEnd in ContainerTempList)
                    ContainerEnds.Add(ContainerTempEnd.GetInventory());

                EngineRaw.GetBlocksOfType(ContainerTempList, container => container.CustomName.Contains("Base"));
                foreach (IMyCargoContainer ContainerTempBase in ContainerTempList)
                    ContainerBases.Add(ContainerTempBase.GetInventory());
            }

            public void ExecuteCargoShift()
            {
                int ballastMass = 123030;
                if (step)
                    foreach (IMyInventory ContainerBase in ContainerBases)
                        foreach (IMyInventory ContainerEnd in ContainerEnds)
                            ContainerBase.TransferItemFrom(ContainerEnd, 0, 0, true, ballastMass / (ContainerBases.Count * ContainerEnds.Count));
                else
                    foreach (IMyInventory ContainerEnd in ContainerEnds)
                        foreach (IMyInventory ContainerBase in ContainerBases)
                            ContainerEnd.TransferItemFrom(ContainerBase, 0, 0, true, ballastMass / (ContainerBases.Count * ContainerEnds.Count));
            }

            public void ExecuteDriver(IMyMotorAdvancedStator driver, float RotorDisplacement)
            {
                driver.SetValue<float>("Displacement", restDisplacement + RotorDisplacement);
            }

            public void ResetDriver(IMyMotorAdvancedStator driver)
            {
                driver.SetValue<float>("Displacement", restDisplacement);
            }
        }
    }
}