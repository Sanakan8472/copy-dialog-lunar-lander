using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Box2DX.Common;
using Box2DX.Collision;
using Box2DX.Dynamics;

namespace CopyDialogLunarLander
{
    [FlagsAttribute]
    enum GameState
    {
        None = 0,
        Active = 1,
        Playing = 4,
    }

    enum ShapeType
    {
        Ground,
        Debris,
        LanderCockpit,
        LanderFoundation,
        LanderLeftLeg,
        LanderRightLeg
    }

    struct GameStats
    {
        public void Init()
        {
            Dead = false;
            Success = false;
            TargetLocation = new Vec2(0, 0);
            CurrentLocation = new Vec2(0, 0);
            Score = 0;

            Fuel = 10000;
            Speed = 0;
            LastStep = 0;
            WorldTime = 0.0f;
            LeftLegOnGroud = false;
            RightLegOnGroud = false;
        }

        public int GetScore()
        {
            return (int)System.Math.Round((Fuel / 10000.0f) * System.Math.Max(1.0f - System.Math.Abs(TargetLocation.X - CurrentLocation.X) / 100.0f, 0.0f) * 1000.0f);
        }


        public bool Dead;
        public bool Success;
        public Vec2 TargetLocation;
        public Vec2 CurrentLocation;
        public float Score;

        public float Fuel;
        public float Speed;

        public float LastStep;
        public float WorldTime;

        public bool LeftLegOnGroud;
        public bool RightLegOnGroud;
    }

    class LunarSim : IDisposable, ContactListener
    {
        class TerrainBlock
        {
            public TerrainBlock(int blockIndex, int blockSize)
            {
                _blockIndex = blockIndex;
                _blockSize = blockSize;
            }

            public void Clear(World world)
            {
                _poi = new Vec2(0,0);
                if (_bodies != null)
                {
                    _bodies.ToList().ForEach(s => { world.DestroyBody(s); });
                    _bodies = null;
                }
            }

            public void Update(World world, AABB _worldAABB, IList<float> segment)
            {
                Clear(world);

                List<Vec2> vertices = new List<Vec2>(segment.Count);
                for (int i = 0; i < segment.Count; ++i)
                {
                    vertices.Add(new Vec2(_blockIndex * _blockSize + i, _worldAABB.Extents.Y * 2 * segment[i]));
                }

                for (int i = vertices.Count - 2; i > 0; --i)
                {
                    float middle = (vertices[i - 1].Y + vertices[i + 1].Y) / 2.0f;
                    if (System.Math.Abs(middle - vertices[i].Y) < 0.05f)
                    {
                        vertices.RemoveAt(i);
                    }
                }

                _bodies = new Body[vertices.Count - 1];
                _poi = vertices[0];
                for (int i = 0; i < vertices.Count - 1; ++i)
                {
                    EdgeDef sd = new EdgeDef();
                    sd.Vertex1 = vertices[i];
                    sd.Vertex2 = vertices[i + 1];
                    sd.Friction = 1;
                    sd.UserData = ShapeType.Ground;
                    BodyDef bd = new BodyDef();
                    bd.Position.Set(0.0f, 0);
                    bd.UserData = ShapeType.Ground;

                    _bodies[i] = world.CreateBody(bd);
                    _bodies[i].CreateFixture(sd);
                }
            }

            public Vec2 GetPointOfInterest()
            {
                return _poi;
            }

            public Body[] _bodies;
            int _blockIndex;
            int _blockSize;
            Vec2 _poi;
        }

        public class Debris
        {
            public Debris(World world, Body body, float lifetime)
            {
                _world = world;
                _body = body;
                _lifetime = lifetime;
            }

            public bool Step(float dt)
            {
                _lifetime -= dt;
                if (_lifetime < 0)
                {
                    _world.DestroyBody(_body);
                    _body = null;
                    return true;
                }
                return false;
            }

            World _world;
            Body _body;
            float _lifetime;
        }

        public static float k_maxSpeed = 5;

        private Random _rnd = new Random();

        private AABB _worldAABB;
        private GameState _state = GameState.None;
        private GameStats _stats;

        private bool _debugMode = false;
        private LunarDraw _draw = new LunarDraw();
        private LunarDebugSceneDraw _debugSceneDraw = new LunarDebugSceneDraw();
        private LunarSceneDraw _lunarSceneDraw = new LunarSceneDraw();
        private World _world;
        private float[] _heightField = null;
        System.Drawing.Color _terrainColor = System.Drawing.Color.Empty;


        private TerrainBlock[] _terrain = null;
        private int _lastBlockWithData = 0;

        private Body _lander = null;
        private List<Debris> _debris = new List<Debris>();

        private bool _left = false;
        private bool _right = false;
        private bool _down = false;

        public LunarSim()
        {
        }

        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool state)
        {
            if (state)
            {
                // By deleting the world, we delete the bomb, mouse joint, etc.
                _world.Dispose();
                _world = null;
            }
        }

        public void Init(System.Windows.Size worldSize)
        {
            _stats.Init();
            _state &= ~GameState.Playing;
            _debugSceneDraw.Flags = DebugDraw.DrawFlags.Shape;
            _lunarSceneDraw.Flags = DebugDraw.DrawFlags.Shape;
            _worldAABB = new AABB();
            _worldAABB.LowerBound.Set(0, 0);
            _worldAABB.UpperBound.Set((float)worldSize.Width, (float)worldSize.Height);
            Vec2 gravity = new Vec2();
            gravity.Set(0.0f, -9.81f);
            bool doSleep = false;

            AABB simAABB = _worldAABB;
            simAABB.LowerBound.X = -100;
            simAABB.UpperBound.X += 200;
            simAABB.UpperBound.Y += 200;
            _world = new World(simAABB, gravity, doSleep);
            _world.SetDebugDraw(_lunarSceneDraw);
            _world.SetContactListener(this);
            {
                // Ground
                PolygonDef sd = new PolygonDef();
                sd.UserData = ShapeType.Ground;
                sd.SetAsBox((float)worldSize.Width, 1);

                BodyDef bd = new BodyDef();
                bd.Position.Set(0.0f, 0);
                bd.UserData = ShapeType.Ground;
                Body ground = _world.CreateBody(bd);
                ground.CreateFixture(sd);
            }
        }

        private Body CreateLander(Vec2 pos)
        {
            float friction = 1;
            // Lander
            PolygonDef sd = new PolygonDef();
            sd.SetAsBox(5, 1, new Vec2(0, -3f), 0);
            sd.Density = 5.0f;
            sd.Friction = friction;

            // Foundation
            BodyDef bd = new BodyDef();
            bd.AngularDamping = 0.5f;
            bd.Position = pos;
            bd.UserData = ShapeType.LanderFoundation;
            Body lander = _world.CreateBody(bd);
            lander.CreateFixture(sd);

            {
                // Cockpit
                CircleDef CapsuleDef = new CircleDef();
                CapsuleDef.LocalPosition = new Vec2(0, 2f);
                CapsuleDef.Radius = 4;
                CapsuleDef.Density = 5.0f;
                CapsuleDef.Friction = friction;
                CapsuleDef.UserData = ShapeType.LanderCockpit;
                lander.CreateFixture(CapsuleDef);
            }
            {
                // Left leg
                PolygonDef legDef = new PolygonDef();
                legDef.SetAsBox(1, 2, new Vec2(-5f, -6f), 0.0f);
                legDef.Density = 2.0f;
                legDef.Friction = friction;
                legDef.UserData = ShapeType.LanderLeftLeg;
                lander.CreateFixture(legDef);
            }
            {
                // Right leg
                PolygonDef legDef = new PolygonDef();
                legDef.SetAsBox(1, 2, new Vec2(5f, -6f), 0.0f);
                legDef.Density = 2.0f;
                legDef.Friction = friction;
                legDef.UserData = ShapeType.LanderRightLeg;
                lander.CreateFixture(legDef);
            }
            lander.SetMassFromShapes();
            return lander;
        }

        void ExplodeLander()
        {
            if (_lander != null)
            {
                Vec2 pos = _stats.CurrentLocation;
                int maxOffset = 10;
                _world.DestroyBody(_lander);
                _lander = null;
                _state &= ~GameState.Playing;

                Vec2[] BoxSizes = new Vec2[3];
                BoxSizes[0] = new Vec2(1, 2);
                BoxSizes[1] = new Vec2(1, 2);
                BoxSizes[2] = new Vec2(5, 1);

                for (int i = 0; i < 3; i++)
                {
                    int force = 6000;
                    Vec2 offset = new Vec2(_rnd.Next(-maxOffset, maxOffset), _rnd.Next(0, maxOffset));
                    var box = AddBox(new System.Windows.Point(pos.X + offset.X, pos.Y + offset.Y), BoxSizes[i].X, BoxSizes[i].Y);
                    _debris.Add(new Debris(_world, box, 5 + 0.1f * _rnd.Next(-10, 10)));
                    box.ApplyImpulse(new Vec2(_rnd.Next(-force, force), _rnd.Next(-force, force)), pos);
                }

                for (int i = 0; i < 7; i++)
                {
                    int force = 6000;
                    Vec2 offset = new Vec2(_rnd.Next(-maxOffset, maxOffset), _rnd.Next(0, maxOffset));
                    var box = AddBox(new System.Windows.Point(pos.X + offset.X, pos.Y + offset.Y));
                    _debris.Add(new Debris(_world, box, 5 + 0.1f * _rnd.Next(-10, 10)));
                    box.ApplyImpulse(new Vec2(_rnd.Next(-force, force), _rnd.Next(-force, force)), pos);
                }
            }
        }

        void WinTheGame()
        {
            if (!_stats.Success && _lander != null)
            {
                _stats.Success = true;
                _lander.SetStatic();
                _state &= ~GameState.Playing;
            }
        }

        public Body Lander
        {
            get { return _lander; }
        }

        public GameState State
        {
            get { return _state; }
        }

        public GameStats Stats
        {
            get { return _stats; }
        }

        public void SetDebug(bool debug)
        {
            _debugMode = debug;
        }

        public void SetActive(bool active)
        {
            if (active)
                _state |= GameState.Active;
            else
                _state &= ~GameState.Active;
        }

        public void SetHeightField(float[] heightField, System.Drawing.Color terrainColor)
        {
            _terrainColor = terrainColor;
            if (_heightField == null)
            {
                _heightField = new float[heightField.Count()];
            }

            int blockSize = 5;
            int blocks = (heightField.Count() - 1) / blockSize; // Minus 1 so we can always access the next block's first element.
            if (_terrain == null || _terrain.Count() != blocks)
            {
                if (_terrain != null)
                {
                    _terrain.ToList().ForEach(s => { s.Clear(_world); });
                }
                _terrain = new TerrainBlock[blocks];
                for (int i = 0; i < blocks; i++)
                    _terrain[i] = new TerrainBlock(i, blockSize);
            }

            for (int i = 0; i < blocks; ++i)
            {
                bool dirty = false;
                // Each block ends where the next starts so the block actually contains blockSize + 1 vertices or blockSize edges.
                for (int p = 0; p <= blockSize; ++p)
                {
                    if (System.Math.Abs(_heightField[i * blockSize + p] - heightField[i * blockSize + p]) > 0.001)
                    {
                        dirty = true;
                        _lastBlockWithData = i;
                        break;
                    }
                }

                if (dirty)
                {
                    _terrain[i].Update(_world, _worldAABB, new ArraySegment<float>(heightField, i * blockSize, blockSize + 1));
                }
            }

            _heightField = heightField;
        }

        public Body AddBox(System.Windows.Point p, float width = 2.5f, float height = 2.5f)
        {
            PolygonDef sd = new PolygonDef();
            sd.SetAsBox(width, height);
            sd.Density = 5.0f;
            sd.UserData = ShapeType.Debris;

            Vec2 pos = new Vec2((float)p.X, (float)p.Y);

            BodyDef bd = new BodyDef();
            bd.Position = pos;
            bd.UserData = ShapeType.Debris;
            Body body = _world.CreateBody(bd);
            body.CreateFixture(sd);
            body.SetMassFromShapes();
            return body;
        }

        public void Input(bool left, bool right, bool down)
        {
            if (!_state.HasFlag(GameState.Active) || _lander == null && _stats.Fuel > 0 || _stats.Dead || _stats.Success)
            {
                _left = false;
                _right = false;
                _down = false;
                return;
            }
            _left = left;
            _right = right;
            _down = down;

            float forceUp = 6000.0f;
            float forceSide = 1000.0f;
            if (down)
            {
                var downVec = _lander.GetWorldVector(new Vec2(0, 1));
                var center = _lander.GetLocalCenter() + _lander.GetPosition();
                _lander.ApplyForce(downVec * forceUp, center);
                _stats.Fuel = System.Math.Max(0, _stats.Fuel - _stats.LastStep * 100);


            }

            if (left || right)
            {
                var center = _lander.GetLocalCenter() + _lander.GetPosition();
                center.Y += 5.0f;
                float force = forceSide;
                if (left)
                    force = -force;
                var forceVec = _lander.GetWorldVector(new Vec2(1, 0));
                _lander.ApplyForce(new Vec2(force, 0), center);
                _stats.Fuel = System.Math.Max(0, _stats.Fuel - _stats.LastStep * 25);
            }
        }

        public void Reset()
        {
            if (_terrain == null)
            {
                return;
            }
            if (_lander != null)
            {
                _world.DestroyBody(_lander);
            }
            int startIndex = _rnd.Next(0, _lastBlockWithData);
            _lander = CreateLander(new Vec2(_terrain[startIndex].GetPointOfInterest().X, 90f));

            _stats.Init();
            int targetIndex = _rnd.Next(0, _lastBlockWithData);
            _stats.TargetLocation = _terrain[targetIndex].GetPointOfInterest();
            _state |= GameState.Playing;
        }

        public void Step(float step, System.Windows.Media.DrawingContext drawingContext)
        {
            if (!_state.HasFlag(GameState.Active))
                return;

            _lunarSceneDraw.DrawingContext = drawingContext;
            _debugSceneDraw.DrawingContext = drawingContext;
            _draw.DrawingContext = drawingContext;

            _lunarSceneDraw.OverrideColor = new Box2DX.Dynamics.Color(_terrainColor.R / 255.0f, _terrainColor.G / 255.0f, _terrainColor.B / 255.0f);
            _world.SetDebugDraw(_debugMode ? (DebugDraw)_debugSceneDraw : (DebugDraw)_lunarSceneDraw);

            _world.SetWarmStarting(true);
            _world.SetContinuousPhysics(true);

            _world.Step(step, 10, 8);

            _world.Validate();

            _stats.LastStep = step;
            _stats.Speed = Lander != null ? Lander.GetLinearVelocity().Length() : 0;
            _stats.WorldTime += step;

            
            for (int i = _debris.Count - 1; i >= 0; i--)
            {
                if (_debris[i].Step(step))
                {
                    _debris.RemoveAt(i);
                }
            }

            // Evaluate game state.
            if (_stats.Dead)
            {
                ExplodeLander();
            }
            else
            {
                if (!_stats.Dead && (_state & GameState.Playing) != 0)
                {
                    // Draw target location marker
                    float width = (float)System.Math.Sin(_stats.WorldTime);
                    Vec2[] points = new Vec2[3];
                    points[0] = _stats.TargetLocation;
                    points[1] = _stats.TargetLocation;
                    points[1].X += 10 * width;
                    points[1].Y += 10;
                    points[2] = _stats.TargetLocation;
                    points[2].X -= 10 * width;
                    points[2].Y += 10;
                    _draw.DrawPolygon(points, 3, new Box2DX.Dynamics.Color(_terrainColor.R / 255.0f, _terrainColor.G / 255.0f, _terrainColor.B / 255.0f));
                }

                if (_lander != null)
                {
                    _stats.CurrentLocation = _lander.GetPosition();

                    Func<XForm, Vec2[]> CreateThruster = transform => 
                    {
                        float width = (float)System.Math.Sin(_stats.WorldTime * 100);
                        Vec2[] points = new Vec2[3];
                        points[0] = new Vec2(0, 0);
                        points[1] = new Vec2(0, 0);
                        points[1].X += 2 * width;
                        points[1].Y -= 10;
                        points[2] = new Vec2(0, 0);
                        points[2].X -= 2 * width;
                        points[2].Y -= 10;
                        for (int i = 0; i < points.Length; i++)
                        {
                            points[i] = transform.Position + Box2DX.Common.Math.Mul(transform.R, points[i]);
                        }
                        return points;
                    };

                    if (_down)
                    {
                        XForm transform = new XForm(_lander.GetWorldPoint(new Vec2(0, -5)), _lander.GetXForm().R);
                        Vec2[] points = CreateThruster(transform);
                        _draw.DrawPolygon(points, 3, new Box2DX.Dynamics.Color(_terrainColor.R / 255.0f, _terrainColor.G / 255.0f, _terrainColor.B / 255.0f));
                    }
                    if (_left)
                    {
                        XForm transform = new XForm(_lander.GetWorldPoint(new Vec2(3, 2)), new Mat22(_lander.GetAngle() + 90.0f * (float)System.Math.PI / 180.0f));
                        Vec2[] points = CreateThruster(transform);
                        _draw.DrawPolygon(points, 3, new Box2DX.Dynamics.Color(_terrainColor.R / 255.0f, _terrainColor.G / 255.0f, _terrainColor.B / 255.0f));
                    }
                    if (_right)
                    {
                        XForm transform = new XForm(_lander.GetWorldPoint(new Vec2(-3, 2)), new Mat22(_lander.GetAngle() - 90.0f * (float)System.Math.PI / 180.0f));
                        Vec2[] points = CreateThruster(transform);
                        _draw.DrawPolygon(points, 3, new Box2DX.Dynamics.Color(_terrainColor.R / 255.0f, _terrainColor.G / 255.0f, _terrainColor.B / 255.0f));
                    }
                }
                if (_stats.LeftLegOnGroud && _stats.RightLegOnGroud)
                {
                    WinTheGame();
                }
            }
        }

        public void Render(System.Windows.Media.DrawingContext drawingContext)
        {
            if ((_state & GameState.Active) != 0)
            {

                if ((_state & GameState.Playing) == 0)
                {
                    if (_stats.Success)
                    {
                        int score = _stats.GetScore();
                        string text;
                        if (score > 0)
                        {
                            text = $"You Won! You score is: {score}";
                        }
                        else
                        {
                            text = $"Game Over! You were too far away from your target!";
                        }
                        LunarSceneDraw.DrawText(drawingContext, text, new System.Windows.Point(3, 3), 12D, _terrainColor);
                    }
                    else if (_stats.Dead)
                    {
                        string text = "Game Over! Your lander exploded!";
                        LunarSceneDraw.DrawText(drawingContext, text, new System.Windows.Point(3, 3), 12D, _terrainColor);
                    }

                    string text2 = "restart = 'Space', thrusters = 'arrow keys'";
                    LunarSceneDraw.DrawText(drawingContext, text2, new System.Windows.Point(3, 16), 12D, _terrainColor);
                }
                else
                {
                    string text = "Speed: " + (_stats.Speed > 0 ? "+" : "-") + System.Math.Abs(_stats.Speed).ToString("0.00 m/s") + " Fuel: " + _stats.Fuel.ToString("00000.0 L");
                    LunarSceneDraw.DrawText(drawingContext, text, new System.Windows.Point(3, 3), 12D, _terrainColor);
                }
            }
            else
            {
                string text = "Paused - click here to play";
                LunarSceneDraw.DrawText(drawingContext, text, new System.Windows.Point(6, 6), 15D, _terrainColor);
            }
        }

        public void BeginContact(Contact contact)
        {
            if (contact.FixtureA?.UserData is ShapeType && contact.FixtureB?.UserData is ShapeType && !_stats.Dead && !_stats.Success)
            {
                ShapeType shapeA = (ShapeType)contact.FixtureA.UserData;
                ShapeType shapeB = (ShapeType)contact.FixtureB.UserData;
                if (shapeA == ShapeType.Ground || shapeA == ShapeType.Debris)
                {
                    var temp = shapeA;
                    shapeA = shapeB;
                    shapeB = temp;
                }

                switch (shapeA)
                {
                    case ShapeType.LanderCockpit:
                    case ShapeType.LanderFoundation:
                        {
                            _stats.Dead = true;
                        }
                        break;
                    case ShapeType.LanderLeftLeg:
                        {
                            if (System.Math.Abs(_stats.Speed) > k_maxSpeed)
                            {
                                _stats.Dead = true;
                            }
                            else
                            {
                                _stats.LeftLegOnGroud = true;
                            }
                        }
                        break;
                    case ShapeType.LanderRightLeg:
                        {
                            if (System.Math.Abs(_stats.Speed) > k_maxSpeed)
                            {
                                _stats.Dead = true;
                            }
                            else
                            {
                                _stats.RightLegOnGroud = true;
                            }
                        }
                        break;
                    default:
                        break;
                }
            }
        }

        public void EndContact(Contact contact)
        {
            if (contact.FixtureA?.UserData is ShapeType && contact.FixtureB?.UserData is ShapeType && !_stats.Dead && !_stats.Success)
            {
                ShapeType shapeA = (ShapeType)contact.FixtureA.UserData;
                ShapeType shapeB = (ShapeType)contact.FixtureB.UserData;
                if (shapeA == ShapeType.Ground || shapeA == ShapeType.Debris)
                {
                    var temp = shapeA;
                    shapeA = shapeB;
                    shapeB = temp;
                }

                switch (shapeA)
                {
                    case ShapeType.LanderCockpit:
                    case ShapeType.LanderFoundation:
                        {
                            _stats.Dead = true;
                        }
                        break;
                    case ShapeType.LanderLeftLeg:
                        {
                            if (System.Math.Abs(_stats.Speed) > k_maxSpeed)
                            {
                                _stats.Dead = true;
                            }
                            else
                            {
                                _stats.LeftLegOnGroud = false;
                            }
                        }
                        break;
                    case ShapeType.LanderRightLeg:
                        {
                            if (System.Math.Abs(_stats.Speed) > k_maxSpeed)
                            {
                                _stats.Dead = true;
                            }
                            else
                            {
                                _stats.RightLegOnGroud = false;
                            }
                        }
                        break;
                    default:
                        break;
                }
            }
        }

        public void PreSolve(Contact contact, Manifold oldManifold)
        {
        }

        public void PostSolve(Contact contact, ContactImpulse impulse)
        {
        }
    }
}
