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
        static string impulseEngineTag = "Impulse Engine";
        static float restDisplacement = -0.2f;
        static float maxDisplacement = -0.2f;
        static int step = 0;
        static bool doRunRefresh = true;
        bool impulseEnginesActive = true;

        IMyShipController mainControl = null;
        List<IMyShipController> controllers = new List<IMyShipController>();

        List<IMyBlockGroup> impulseEnginesRaw = new List<IMyBlockGroup>();
        List<ImpulseEngine> impulseEngines = new List<ImpulseEngine>();

        Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update1;
        }

        void Main(string argument)
        {
            switch (argument.ToUpper())
            {
                case "TOGGLE":
                    impulseEnginesActive = !impulseEnginesActive;
                    break;

                default:
                    break;
            }
            if (doRunRefresh)
            {
                RefreshBlocks();
                doRunRefresh = false;
            }

            //Control input vector relative to grid
            Vector3D pilotInputLocal = mainControl.MoveIndicator;
            //Normalized control input vector relative to world
            Vector3D pilotInputWorld = mainControl.WorldMatrix.Backward * pilotInputLocal.Z + mainControl.WorldMatrix.Right * pilotInputLocal.X + mainControl.WorldMatrix.Up * pilotInputLocal.Y;
            if (pilotInputWorld.LengthSquared() > 0)
                pilotInputWorld = Vector3D.Normalize(pilotInputWorld);
            //Ships normalized velocity vector relative to world
            Vector3D shipVelocity = mainControl.GetShipVelocities().LinearVelocity;
            if (shipVelocity.LengthSquared() > 0)
                shipVelocity = Vector3D.Normalize(shipVelocity);

            if (impulseEnginesActive)
                ApplyThrust(shipVelocity, pilotInputWorld, mainControl.GetShipSpeed());
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

            GridTerminalSystem.GetBlockGroups(impulseEnginesRaw, impulseEngine => impulseEngine.Name.Contains(impulseEngineTag));
            foreach (IMyBlockGroup impulseEngineRaw in impulseEnginesRaw)
                impulseEngines.Add(new ImpulseEngine(impulseEngineRaw));
        }

        void ApplyThrust(Vector3D shipVelocity, Vector3D pilotInputWorld, double shipSpeed)
        {
            foreach (ImpulseEngine impulseEngine in impulseEngines)
            {
                try
                {

                    int ballastMass = 123030;

                    foreach (IMyMotorAdvancedStator impulseDriver in impulseEngine.ImpulseDrivers)
                    {
                        //Which direction is this driver pointing relative to the world?
                        Vector3D impulseEngineActingVector = impulseDriver.WorldMatrix.Up;
                        double impulseEngineDotPilotInput = impulseEngineActingVector.Dot(pilotInputWorld);
                        double impulseEngineDotShipVelocityScaled = impulseEngineActingVector.Dot(shipVelocity) * shipSpeed;
                        float rotorDisplacement;

                        //Is this driver going to help accelerate in the intended direction?
                        if (impulseEngineDotPilotInput > 0.05)
                        {
                            impulseEngine.ExecuteDriver(impulseDriver, -maxDisplacement);
                        }
                        else if (-impulseEngineDotPilotInput > 0.05)
                        {
                            impulseEngine.ExecuteDriver(impulseDriver, maxDisplacement);
                        }
                        else
                        {
                            //Is this driver going to help dampers stop?
                            if (-impulseEngineDotPilotInput == 0 && -impulseEngineDotShipVelocityScaled > 0.01)
                            {
                                rotorDisplacement = -maxDisplacement;
                                if (-impulseEngineDotShipVelocityScaled < 15)
                                    rotorDisplacement = (float)(-maxDisplacement * (-impulseEngineDotShipVelocityScaled / 15));

                                impulseEngine.ExecuteDriver(impulseDriver, rotorDisplacement);
                            }
                            else if (impulseEngineDotPilotInput == 0 && impulseEngineDotShipVelocityScaled > 0.01)
                            {
                                rotorDisplacement = maxDisplacement;
                                if (impulseEngineDotShipVelocityScaled < 15)
                                    rotorDisplacement = (float)(maxDisplacement * impulseEngineDotShipVelocityScaled / 15);

                                impulseEngine.ExecuteDriver(impulseDriver, rotorDisplacement);
                            }
                            else
                                impulseEngine.ResetDriver(impulseDriver);
                        }
                        impulseDriver.Attach();
                    }

                    if (step == 0)
                        foreach (IMyInventory ContainerBase in impulseEngine.ContainerBases)
                            foreach (IMyInventory ContainerEnd in impulseEngine.ContainerEnds)
                                ContainerBase.TransferItemFrom(ContainerEnd, 0, 0, true, ballastMass / (impulseEngine.ContainerBases.Count * impulseEngine.ContainerEnds.Count));
                    else
                        foreach (IMyInventory ContainerEnd in impulseEngine.ContainerEnds)
                            foreach (IMyInventory ContainerBase in impulseEngine.ContainerBases)
                                ContainerEnd.TransferItemFrom(ContainerBase, 0, 0, true, ballastMass / (impulseEngine.ContainerBases.Count * impulseEngine.ContainerEnds.Count));
                }
                catch
                {
                    doRunRefresh = true;
                    Me.CustomData = ("Encountered Error at " + DateTime.Now);
                }
            }
            if (step == 0)
            {
                step = 1;
                maxDisplacement = 0.2f;
            }
            else
            {
                step = 0;
                maxDisplacement = -0.2f;
            }
        }

        class ImpulseEngine
        {
            public IMyBlockGroup ImpulseEngineRaw { get; set; }
            public List<IMyMotorAdvancedStator> ImpulseDrivers { get; set; } = new List<IMyMotorAdvancedStator>();
            public List<IMyCargoContainer> ContainerTempList { get; set; } = new List<IMyCargoContainer>();
            public List<IMyInventory> ContainerEnds { get; set; } = new List<IMyInventory>();
            public List<IMyInventory> ContainerBases { get; set; } = new List<IMyInventory>();

            public ImpulseEngine(IMyBlockGroup impulseEngine)
            {
                ImpulseEngineRaw = impulseEngine;
                PopulateEngine();
            }

            public void PopulateEngine()
            {
                ImpulseEngineRaw.GetBlocksOfType(ImpulseDrivers);

                ImpulseEngineRaw.GetBlocksOfType(ContainerTempList, ImpulseContainer => ImpulseContainer.CustomName.Contains("End"));
                foreach (IMyCargoContainer ContainerTempEnd in ContainerTempList)
                    ContainerEnds.Add(ContainerTempEnd.GetInventory());

                ImpulseEngineRaw.GetBlocksOfType(ContainerTempList, ImpulseContainer => ImpulseContainer.CustomName.Contains("Base"));
                foreach (IMyCargoContainer ContainerTempBase in ContainerTempList)
                    ContainerBases.Add(ContainerTempBase.GetInventory());
            }

            public void ExecuteDriver(IMyMotorAdvancedStator impulseDriver, float RotorDisplacement)
            {
                impulseDriver.SetValue<float>("Displacement", restDisplacement + RotorDisplacement);
            }

            public void ResetDriver(IMyMotorAdvancedStator impulseDriver)
            {
                impulseDriver.SetValue<float>("Displacement", restDisplacement);
            }
        }
    }
}