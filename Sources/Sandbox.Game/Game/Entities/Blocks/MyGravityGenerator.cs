﻿#region Using

using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Havok;
using Sandbox.Common;

using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Graphics.GUI;
using Sandbox.Engine.Physics;
using Sandbox.Engine.Utils;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.GameSystems.Electricity;
using Sandbox.Game.Multiplayer;

using VRage.Trace;
using VRageMath;
using Sandbox.Game.World;
using Sandbox.Game.Gui;
using Sandbox.Game.Screens;
using System.Diagnostics;
using System;
using VRageRender;
using Sandbox.Game.Screens.Terminal.Controls;
using Sandbox.Game.Components;
using Sandbox.ModAPI.Ingame;
using Sandbox.Game.Localization;
using VRage;
using VRage.Utils;
using Sandbox.Game.GameSystems;

#endregion

namespace Sandbox.Game.Entities
{
    [MyCubeBlockType(typeof(MyObjectBuilder_GravityGenerator))]
    class MyGravityGenerator : MyGravityGeneratorBase, IMyPowerConsumer, IMyGravityGenerator
    {
        private const int NUM_DECIMALS = 0;
        private new MyGravityGeneratorDefinition BlockDefinition
        {
            get { return (MyGravityGeneratorDefinition)base.BlockDefinition; }
        }

        private new MySyncGravityGenerator SyncObject;
        private BoundingBox m_gizmoBoundingBox = new BoundingBox();

        private Vector3 m_fieldSize = new Vector3(150f);
        public Vector3 FieldSize
        {
            get { return m_fieldSize; }
            set
            {
                if (m_fieldSize != value)
                {
                    m_fieldSize = value;
                    UpdateFieldShape();
                    RaisePropertiesChanged();
                }
            }
        }
        
        public override BoundingBox? GetBoundingBox()  
        {
            m_gizmoBoundingBox.Min = PositionComp.LocalVolume.Center - FieldSize / 2.0f;
            m_gizmoBoundingBox.Max = PositionComp.LocalVolume.Center + FieldSize / 2.0f;
            return m_gizmoBoundingBox;
        }

        static MyGravityGenerator()
        {
            var fieldWidth = new MyTerminalControlSlider<MyGravityGenerator>("Width", MySpaceTexts.BlockPropertyTitle_GravityFieldWidth, MySpaceTexts.BlockPropertyDescription_GravityFieldWidth);
            fieldWidth.SetLimits(1, 150);
            fieldWidth.DefaultValue = 150;
            fieldWidth.Getter = (x) => x.m_fieldSize.X;
            fieldWidth.Setter = (x, v) =>
            {
                x.m_fieldSize.X = v;
                x.SyncObject.SendChangeGravityGeneratorRequest(ref x.m_fieldSize, x.GravityAcceleration);
            };
            fieldWidth.Writer = (x, result) => result.Append(MyValueFormatter.GetFormatedFloat(x.m_fieldSize.X, NUM_DECIMALS)).Append(" m");
            fieldWidth.EnableActions();
            MyTerminalControlFactory.AddControl(fieldWidth);

            var fieldHeight = new MyTerminalControlSlider<MyGravityGenerator>("Height", MySpaceTexts.BlockPropertyTitle_GravityFieldHeight, MySpaceTexts.BlockPropertyDescription_GravityFieldHeight);
            fieldHeight.SetLimits(1, 150);
            fieldHeight.DefaultValue = 150;
            fieldHeight.Getter = (x) => x.m_fieldSize.Y;
            fieldHeight.Setter = (x, v) =>
            {
                x.m_fieldSize.Y = v;
                x.SyncObject.SendChangeGravityGeneratorRequest(ref x.m_fieldSize, x.GravityAcceleration);
            };
            fieldHeight.Writer = (x, result) => result.Append(MyValueFormatter.GetFormatedFloat(x.m_fieldSize.Y, NUM_DECIMALS)).Append(" m");

            fieldHeight.EnableActions();
            MyTerminalControlFactory.AddControl(fieldHeight);

            var fieldDepth = new MyTerminalControlSlider<MyGravityGenerator>("Depth", MySpaceTexts.BlockPropertyTitle_GravityFieldDepth, MySpaceTexts.BlockPropertyDescription_GravityFieldDepth);
            fieldDepth.SetLimits(1, 150);
            fieldDepth.DefaultValue = 150;
            fieldDepth.Getter = (x) => x.m_fieldSize.Z;
            fieldDepth.Setter = (x, v) =>
            {
                x.m_fieldSize.Z = v;
                x.SyncObject.SendChangeGravityGeneratorRequest(ref x.m_fieldSize, x.GravityAcceleration);
            };
            fieldDepth.Writer = (x, result) => result.Append(MyValueFormatter.GetFormatedFloat(x.m_fieldSize.Z, NUM_DECIMALS)).Append(" m");
            fieldDepth.EnableActions();
            MyTerminalControlFactory.AddControl(fieldDepth);

            var gravityAcceleration = new MyTerminalControlSlider<MyGravityGenerator>("Gravity", MySpaceTexts.BlockPropertyTitle_GravityAcceleration, MySpaceTexts.BlockPropertyDescription_GravityAcceleration);
            gravityAcceleration.SetLimits(-1, 1);
            gravityAcceleration.DefaultValue = 1;
            gravityAcceleration.Getter = (x) => x.GravityAcceleration / MyGravityProviderSystem.G;
            gravityAcceleration.Setter = (x, v) => x.SyncObject.SendChangeGravityGeneratorRequest(ref x.m_fieldSize, v * MyGravityProviderSystem.G);
            gravityAcceleration.Writer = (x, result) => result.AppendDecimal(x.m_gravityAcceleration / MyGravityProviderSystem.G, 2).Append(" G");
            gravityAcceleration.EnableActions();
            MyTerminalControlFactory.AddControl(gravityAcceleration);
        }
        public override void Init(MyObjectBuilder_CubeBlock objectBuilder, MyCubeGrid cubeGrid)
        {
            var builder = (MyObjectBuilder_GravityGenerator)objectBuilder;
            m_fieldSize = builder.FieldSize;
            m_gravityAcceleration = builder.GravityAcceleration;

            base.Init(objectBuilder, cubeGrid);

            SyncObject = new MySyncGravityGenerator(this);
            
            PowerReceiver = new MyPowerReceiver(
                MyConsumerGroupEnum.Utility,
                false,
                BlockDefinition.RequiredPowerInput,
                this.CalculateRequiredPowerInput);
            if (CubeGrid.CreatePhysics)
            {
                PowerReceiver.IsPoweredChanged += Receiver_IsPoweredChanged;
                PowerReceiver.RequiredInputChanged += Receiver_RequiredInputChanged;
                PowerReceiver.Update();
                AddDebugRenderComponent(new MyDebugRenderComponentDrawPowerReciever(PowerReceiver, this));
            }
        }

        public override MyObjectBuilder_CubeBlock GetObjectBuilderCubeBlock(bool copy = false)
        {
            var builder = (MyObjectBuilder_GravityGenerator)base.GetObjectBuilderCubeBlock(copy);

            builder.FieldSize = m_fieldSize;
            builder.GravityAcceleration = m_gravityAcceleration;

            return builder;
        }

      
        protected override float CalculateRequiredPowerInput()
        {
            if (Enabled && IsFunctional)
                return 0.0003f * Math.Abs(m_gravityAcceleration) * (float)Math.Pow(m_fieldSize.Volume, 0.35);
            else
                return 0.0f;
        }

        protected override void UpdateText()
        {
            DetailedInfo.Clear();
            DetailedInfo.AppendStringBuilder(MyTexts.Get(MySpaceTexts.BlockPropertiesText_Type));
            DetailedInfo.Append(BlockDefinition.DisplayNameText);
            DetailedInfo.Append("\n");
            DetailedInfo.AppendStringBuilder(MyTexts.Get(MySpaceTexts.BlockPropertiesText_MaxRequiredInput));
            MyValueFormatter.AppendWorkInBestUnit(PowerReceiver.MaxRequiredInput, DetailedInfo);
            DetailedInfo.Append("\n");
            DetailedInfo.AppendStringBuilder(MyTexts.Get(MySpaceTexts.BlockPropertyProperties_CurrentInput));
            MyValueFormatter.AppendWorkInBestUnit(PowerReceiver.IsPowered ? PowerReceiver.RequiredInput : 0, DetailedInfo);
            RaisePropertiesChanged();
        }

        public override bool IsPositionInRange(Vector3D worldPoint)
        {
            Vector3 halfExtents = m_fieldSize * 0.5f;
            MyOrientedBoundingBox obb = new MyOrientedBoundingBox((Vector3)WorldMatrix.Translation, halfExtents, Quaternion.CreateFromRotationMatrix(WorldMatrix));
            Vector3 conv = (Vector3)worldPoint;
            return obb.Contains(ref conv);
        }

        public override Vector3 GetWorldGravity(Vector3D worldPoint)
        {
            return Vector3.TransformNormal(Vector3.Down * GravityAcceleration, WorldMatrix);
        }

        protected override HkShape GetHkShape()
        {
            return new HkBoxShape(m_fieldSize * 0.5f);
        }

        float IMyGravityGenerator.FieldWidth { get { return m_fieldSize.X; } }
        float IMyGravityGenerator.FieldHeight { get { return m_fieldSize.Y; } }
        float IMyGravityGenerator.FieldDepth { get { return m_fieldSize.Z; } }
        float IMyGravityGenerator.Gravity { get { return GravityAcceleration / MyGravityProviderSystem.G; } }
    }
}

