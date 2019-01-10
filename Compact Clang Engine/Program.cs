﻿using Sandbox.Game.EntityComponents;
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
            Me.CustomData += Runtime.LastRunTimeMs;
            switch (argument.ToUpper())
            {
                case "TOGGLE":
                    impulseEnginesActive = !impulseEnginesActive;
                    break;
            }
            if (doRunRefresh)
            {
                RefreshBlocks();
                doRunRefresh = false;
            }
            float massShip = mainControl.CalculateShipMass().PhysicalMass;

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
            Me.CustomData += "\n";
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

                    foreach (ImpulseDriver impulseDriver in impulseEngine.ImpulseDrivers)
                    {
                        //Which direction is this driver pointing relative to the world?
                        Vector3D impulseEngineActingVector = impulseDriver.ImpulseDriverRaw.WorldMatrix.Up;
                        double impulseEngineDotPilotInput = impulseEngineActingVector.Dot(pilotInputWorld);
                        double impulseEngineDotShipVelocityScaled = impulseEngineActingVector.Dot(shipVelocity) * shipSpeed;

                        //Is this driver going to help accelerate in the intended direction?
                        if (impulseEngineDotPilotInput > 0.05)
                        {
                            impulseDriver.RotorDisplacement = -maxDisplacement;
                            impulseDriver.Active = true;
                        }
                        else if (-impulseEngineDotPilotInput > 0.05)
                        {
                            impulseDriver.RotorDisplacement = maxDisplacement;
                            impulseDriver.Active = true;
                        }
                        else
                        {
                            //Is this driver going to help dampers stop?
                            if (-impulseEngineDotPilotInput == 0 && -impulseEngineDotShipVelocityScaled > 0.01)
                            {
                                impulseDriver.RotorDisplacement = -maxDisplacement;
                                if (-impulseEngineDotShipVelocityScaled < 15)
                                    impulseDriver.RotorDisplacement = (float)(-maxDisplacement * (-impulseEngineDotShipVelocityScaled / 15));

                                impulseDriver.Active = true;
                            }
                            else if (impulseEngineDotPilotInput == 0 && impulseEngineDotShipVelocityScaled > 0.01)
                            {
                                impulseDriver.RotorDisplacement = maxDisplacement;
                                if (impulseEngineDotShipVelocityScaled < 15)
                                    impulseDriver.RotorDisplacement = (float)(maxDisplacement * impulseEngineDotShipVelocityScaled / 15);

                                impulseDriver.Active = true;
                            }
                            else
                                impulseDriver.Active = false;
                        }
                    }

                    if (step == 0)
                        foreach (IMyInventory ContainerEnd in impulseEngine.ContainerEnds)
                            impulseEngine.ContainerBase.TransferItemFrom(ContainerEnd, 0, 0);
                    else
                        foreach (IMyInventory ContainerEnd in impulseEngine.ContainerEnds)
                            ContainerEnd.TransferItemFrom(impulseEngine.ContainerBase, 0, 0, true, ballastMass / impulseEngine.ContainerEnds.Count);

                    foreach (ImpulseDriver impulseDriver in impulseEngine.ImpulseDrivers)
                    {
                        if (impulseDriver.Active)
                            impulseDriver.SyncDisplacement();
                        else
                            impulseDriver.ZeroDisplacement();
                        impulseDriver.ImpulseDriverRaw.Attach();
                    }
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
            public List<IMyMotorAdvancedStator> ImpulseDriversRaw { get; set; } = new List<IMyMotorAdvancedStator>();
            public List<ImpulseDriver> ImpulseDrivers { get; set; } = new List<ImpulseDriver>();
            public List<IMyCargoContainer> ContainerTempList { get; set; } = new List<IMyCargoContainer>();
            public List<IMyInventory> ContainerEnds { get; set; } = new List<IMyInventory>();
            public IMyInventory ContainerBase { get; set; }

            public ImpulseEngine(IMyBlockGroup impulseEngine)
            {
                ImpulseEngineRaw = impulseEngine;
                PopulateEngine();
            }

            public void PopulateEngine()
            {
                ImpulseEngineRaw.GetBlocksOfType(ImpulseDriversRaw);
                foreach (IMyMotorAdvancedStator impulseDriverRaw in ImpulseDriversRaw)
                    ImpulseDrivers.Add(new ImpulseDriver(impulseDriverRaw));

                ImpulseEngineRaw.GetBlocksOfType(ContainerTempList, BaseImpulseContainer => BaseImpulseContainer.CustomName.Contains("End"));
                foreach (IMyCargoContainer ContainerTemp in ContainerTempList)
                    ContainerEnds.Add(ContainerTemp.GetInventory());

                ImpulseEngineRaw.GetBlocksOfType(ContainerTempList, BaseImpulseContainer => BaseImpulseContainer.CustomName.Contains("Base"));
                ContainerBase = ContainerTempList[0].GetInventory();
            }

        }

        class ImpulseDriver
        {
            public IMyMotorAdvancedStator ImpulseDriverRaw { get; set; }
            public float RotorDisplacement { get; set; }
            public bool Active { get; set; }

            public ImpulseDriver(IMyMotorAdvancedStator impulseDriver)
            {
                ImpulseDriverRaw = impulseDriver;
                RotorDisplacement = 0.0f;
            }

            public void ZeroDisplacement()
            {
                ImpulseDriverRaw.Displacement = restDisplacement;
                RotorDisplacement = 0.0f;
            }

            public void SyncDisplacement()
            {
                ImpulseDriverRaw.Displacement = restDisplacement + RotorDisplacement;
            }
        }
    }
}