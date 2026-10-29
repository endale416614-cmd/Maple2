using System.Numerics;
using DotRecast.Core;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Detour.Crowd;
using Maple2.Server.Game.Model;
using Maple2.Tools.DotRecast;
using Maple2.Tools.Extensions;
using Serilog;

namespace Maple2.Server.Game.Manager.Field;

public sealed class AgentNavigation {
    private static readonly ILogger Logger = Log.Logger.ForContext<AgentNavigation>();

    private readonly FieldNpc npc;
    private FieldManager field => npc.Field;
    public readonly DtCrowdAgent agent;
    private readonly DtCrowd crowd;

    public List<RcVec3f>? currentPath = [];
    private int currentPathIndex = 0;
    private float currentPathProgress = 0;

    public AgentNavigation(FieldNpc fieldNpc, DtCrowdAgent dtAgent, DtCrowd dtCrowd) {
        npc = fieldNpc;
        agent = dtAgent;
        crowd = dtCrowd;
    }

    public List<RcVec3f>? FindPath(Vector3 startVec, Vector3 targetVec) {
        return FindPath(crowd, DotRecastHelper.ToNavMeshSpace(startVec), DotRecastHelper.ToNavMeshSpace(targetVec));
    }

    public List<RcVec3f>? FindPath(RcVec3f startVec, RcVec3f targetVec) {
        return FindPath(crowd, startVec, targetVec);
    }

    private List<RcVec3f>? FindPath(DtCrowd crowd, RcVec3f startVec, RcVec3f targetVec) {
        DtNavMesh navMesh = crowd.GetNavMesh();
        DtNavMeshQuery navMeshQuery = crowd.GetNavMeshQuery();
        IDtQueryFilter filter = crowd.GetFilter(0);
        if (!TryFindPoly(startVec, out long pos1Ref, out RcVec3f _)) {
            Logger.Error("Failed to find nearest poly at {StartVec}", startVec);
            return null;
        }

        if (!TryFindPoly(targetVec, out long posRef2, out RcVec3f _)) {
            Logger.Error("Failed to find nearest poly at {TargetVec}", targetVec);
            return null;
        }

        List<long> pathIterPolys = [];
        navMeshQuery.FindPath(pos1Ref, posRef2, startVec, targetVec, filter, ref pathIterPolys, new DtFindPathOption(0, float.MaxValue));
        if (pathIterPolys.Count == 0) {
            Logger.Error("Failed to find path from {StartVec} to {TargetVec}", startVec, targetVec);
            return null;
        }

        int pathIterPolysCount = pathIterPolys.Count;

        navMeshQuery.ClosestPointOnPoly(pos1Ref, startVec, out RcVec3f iterPos, out bool _);
        navMeshQuery.ClosestPointOnPoly(pathIterPolys[pathIterPolysCount - 1], targetVec, out RcVec3f endPos, out bool _);

        Span<long> visited = stackalloc long[16];
        int nvisited = 0;

        int MAX_POLYS = 256;
        int MAX_SMOOTH = 256;
        List<RcVec3f> smoothPath = [];
        while (pathIterPolysCount > 0 && smoothPath.Count < MAX_SMOOTH) {
            // Find location to steer towards.
            if (!DtPathUtils.GetSteerTarget(navMeshQuery, iterPos, endPos, DotRecastHelper.MIN_TARGET_DIST,
                pathIterPolys, pathIterPolysCount, out var steerPos, out int steerPosFlag, out long steerPosRef)) {
                break;
            }

            bool endOfPath = (steerPosFlag & DtStraightPathFlags.DT_STRAIGHTPATH_END) != 0;
            bool offMeshConnection = (steerPosFlag & DtStraightPathFlags.DT_STRAIGHTPATH_OFFMESH_CONNECTION) != 0;

            // Find movement delta.
            RcVec3f delta = RcVec3f.Subtract(steerPos, iterPos);
            float len = MathF.Sqrt(RcVec3f.Dot(delta, delta));
            // If the steer target is end of path or off-mesh link, do not move past the location.
            if ((endOfPath || offMeshConnection) && len < DotRecastHelper.STEP_SIZE) {
                len = 1;
            } else {
                len = DotRecastHelper.STEP_SIZE / len;
            }

            RcVec3f moveTgt = RcVecUtils.Mad(iterPos, delta, len);

            // Move
            navMeshQuery.MoveAlongSurface(pathIterPolys[0], iterPos, moveTgt, filter, out var result, visited, out nvisited, 16);

            iterPos = result;

            pathIterPolysCount = DtPathUtils.MergeCorridorStartMoved(ref pathIterPolys, pathIterPolysCount, MAX_POLYS, visited, nvisited);
            pathIterPolysCount = DtPathUtils.FixupShortcuts(ref pathIterPolys, pathIterPolysCount, navMeshQuery);

            if (navMeshQuery.GetPolyHeight(pathIterPolys[0], result, out float h).Succeeded()) {
                iterPos.Y = h;
            }

            // Handle end of path and off-mesh links when close enough.
            if (endOfPath && DtPathUtils.InRange(iterPos, steerPos, DotRecastHelper.MIN_TARGET_DIST, 1.0f)) {
                // Reached end of path.
                iterPos = targetVec;
                if (smoothPath.Count < MAX_SMOOTH) {
                    smoothPath.Add(iterPos);
                }

                break;
            } else if (offMeshConnection && DtPathUtils.InRange(iterPos, steerPos, DotRecastHelper.MIN_TARGET_DIST, 1.0f)) {
                // Reached off-mesh connection.
                RcVec3f startPosition = RcVec3f.Zero;
                RcVec3f endPosition = RcVec3f.Zero;

                // Advance the path up to and over the off-mesh connection.
                long prevRef = 0;
                long polyRef = pathIterPolys[0];
                int npos = 0;
                while (npos < pathIterPolysCount && polyRef != steerPosRef) {
                    prevRef = polyRef;
                    polyRef = pathIterPolys[npos];
                    npos++;
                }

                pathIterPolys = pathIterPolys.GetRange(npos, pathIterPolys.Count - npos);
                pathIterPolysCount -= npos;

                // Handle the connection.
                var status4 = navMesh.GetOffMeshConnectionPolyEndPoints(prevRef, polyRef, ref startPosition, ref endPosition);
                if (status4.Succeeded()) {
                    if (smoothPath.Count < MAX_SMOOTH) {
                        smoothPath.Add(startPosition);
                        // Hack to make the dotted path not visible during off-mesh connection.
                        if ((smoothPath.Count & 1) != 0) {
                            smoothPath.Add(startPosition);
                        }
                    }

                    // Move position at the other side of the off-mesh link.
                    iterPos = endPosition;
                    navMeshQuery.GetPolyHeight(pathIterPolys[0], iterPos, out float eh);
                    iterPos.Y = eh;
                }
            }

            // Store results.
            if (smoothPath.Count < MAX_SMOOTH) {
                smoothPath.Add(iterPos);
            }
        }

        return smoothPath;
    }

    public bool HasPath => currentPath != null && currentPath.Count > 0;

    public bool UpdatePosition() => UpdatePosition(npc.Position);

    /// <summary>Puts the NPC and its agent on the navmesh at <paramref name="position"/>. False, and the NPC stays where it is,
    /// when no polygon is within reach of it.</summary>
    public bool UpdatePosition(Vector3 position) {
        if (!FindOnMesh(crowd.GetNavMeshQuery(), crowd.GetFilter(0), DotRecastHelper.ToNavMeshSpace(position), out RcVec3f onMesh)) {
            Logger.Debug("No navmesh within reach of {Position}: the NPC stays at {Current}", position, npc.Position);
            return false;
        }

        agent.npos = onMesh;
        npc.Position = DotRecastHelper.FromNavMeshSpace(onMesh);
        return true;
    }

    /// <summary>Puts the NPC down after a jump or a pull from <paramref name="start"/> to <paramref name="end"/>: on the navmesh
    /// at the end, else back where it took off. False when neither is on the navmesh.</summary>
    public bool Land(Vector3 start, Vector3 end) {
        if (!FindLanding(crowd.GetNavMeshQuery(), crowd.GetFilter(0), DotRecastHelper.ToNavMeshSpace(start), DotRecastHelper.ToNavMeshSpace(end), out RcVec3f landing)) {
            return false;
        }

        agent.npos = landing;
        npc.Position = DotRecastHelper.FromNavMeshSpace(landing);
        return true;
    }

    /// <summary>FieldManager.FindNearestPoly's search box in navmesh space: 200 units across and 400 up and down.</summary>
    public static readonly RcVec3f SearchExtents = new(2, 4, 2);

    /// <summary>
    /// The navmesh point nearest <paramref name="point"/> within <see cref="SearchExtents"/>, all in navmesh space. False when no
    /// polygon is in the box: Detour then reports success with ref 0 and hands the point back unchanged, and a move must not keep
    /// that point (BT-67).
    /// </summary>
    public static bool FindOnMesh(DtNavMeshQuery query, IDtQueryFilter filter, RcVec3f point, out RcVec3f onMesh) {
        onMesh = point;
        return point.IsFinite()
            && query.FindNearestPoly(point, SearchExtents, filter, out long polyRef, out onMesh, out _).Succeeded()
            && polyRef != 0;
    }

    /// <summary>Where a jump from <paramref name="start"/> to <paramref name="end"/> lands, in navmesh space: on the navmesh at the
    /// end, else on it at the start. False when neither is on the navmesh.</summary>
    public static bool FindLanding(DtNavMeshQuery query, IDtQueryFilter filter, RcVec3f start, RcVec3f end, out RcVec3f landing) {
        return FindOnMesh(query, filter, end, out landing) || FindOnMesh(query, filter, start, out landing);
    }

    public Vector3 GetAgentPosition() {
        return DotRecastHelper.FromNavMeshSpace(agent.npos);
    }

    public (Vector3 Start, Vector3 End) Advance(TimeSpan timeSpan, float speed, out bool followSegment) {
        followSegment = false;
        if (currentPath?.Count < 2) {
            return default;
        }

        Vector3 start = DotRecastHelper.FromNavMeshSpace(currentPath![currentPathIndex]);

        if (currentPathIndex >= currentPath.Count - 1) {
            return default;
        }

        followSegment = true;
        float distance = RcVec3f.Distance(currentPath[currentPathIndex], currentPath[currentPathIndex + 1]);

        speed *= DotRecastHelper.MapRotation.GetRightAxis().Length(); // changing speed to navmesh space
        float timeLeft = distance * (1 - currentPathProgress) / speed;
        while (timeLeft < timeSpan.TotalSeconds) {
            timeSpan -= TimeSpan.FromSeconds(timeLeft);
            currentPathIndex++;
            currentPathProgress = 0;

            if (currentPathIndex >= currentPath.Count - 1) {
                return (start, DotRecastHelper.FromNavMeshSpace(currentPath[^1]));
            }

            distance = RcVec3f.Distance(currentPath[currentPathIndex], currentPath[currentPathIndex + 1]);
            timeLeft = distance * (1 - currentPathProgress) / speed;
        }

        currentPathProgress += (float) timeSpan.TotalSeconds * speed / distance;
        RcVec3f end = RcVec3f.Lerp(currentPath[currentPathIndex], currentPath[currentPathIndex + 1], currentPathProgress);

        return (start, DotRecastHelper.FromNavMeshSpace(end));
    }

    /// <summary>
    /// Advances the actor in a straight line towards the target position, ignoring navmesh.
    /// </summary>
    /// <param name="start">Current position</param>
    /// <param name="target">Target position</param>
    /// <param name="speed">Movement speed</param>
    /// <param name="deltaTime">Time in seconds</param>
    /// <returns>Tuple of (new position, reachedTarget)</returns>
    public (Vector3 newPosition, bool reachedTarget) FlyAdvance(Vector3 start, Vector3 target, float speed, float deltaTime) {
        Vector3 direction = target - start;
        float distance = direction.Length();
        if (distance == 0) return (target, true);

        float moveDist = speed * deltaTime;
        if (moveDist >= distance) {
            return (target, true);
        }
        Vector3 step = direction / distance * moveDist;
        return (start + step, false);
    }

    public Vector3 GetRandomPatrolPoint() {
        if (!field.FindNearestPoly(npc.Origin, out long startRef, out RcVec3f startVec)) {
            return npc.Position;
        }

        float moveArea = npc.Value.Metadata.Action.MoveArea;
        if (moveArea == 0) {
            return npc.Origin;
        }

        moveArea *= DotRecastHelper.MapRotation.GetRightAxis().Length(); // changing moveArea to navmesh space
        DtStatus end = crowd.GetNavMeshQuery().FindRandomPointWithinCircle(startRef, startVec, moveArea, crowd.GetFilter(0), new RcRand(), out _, out RcVec3f randomPt);
        if (end.Failed()) {
            return npc.Origin;
        }

        return DotRecastHelper.FromNavMeshSpace(randomPt);
    }

    public Vector3 FindRandomPointAround(Vector3 point, int maxDistance, Vector3 fallback) {
        if (!field.FindNearestPoly(point, out long closest, out RcVec3f position)) {
            return fallback;
        }

        float distance = maxDistance * DotRecastHelper.MapRotation.GetRightAxis().Length(); // changing distance to navmesh space
        var status = crowd.GetNavMeshQuery().FindRandomPointAroundCircle(closest, position, distance, crowd.GetFilter(0), new RcRand(), out _, out RcVec3f randomPt);

        if (status.Failed()) {
            return fallback;
        }

        return DotRecastHelper.FromNavMeshSpace(randomPt);
    }

    public Vector3 FindRandomPointAround(Vector3 point, int maxDistance) {
        return FindRandomPointAround(point, maxDistance, DotRecastHelper.FromNavMeshSpace(agent.npos));
    }

    /// <summary>
    /// Nearest navmesh polygon to <paramref name="point"/>. Detour's FindNearestPoly reports success with ref 0 (and hands
    /// the query point back unchanged) when no polygon is inside the search box, so a point off the navmesh, such as a
    /// player standing on scenery the mesh does not cover, has to be rejected here or FindPath fails on it.
    /// </summary>
    private bool TryFindPoly(RcVec3f point, out long polyRef, out RcVec3f position) {
        return field.FindNearestPoly(point, out polyRef, out position) && polyRef != 0;
    }

    private bool TryFindPoly(Vector3 point, out long polyRef, out RcVec3f position) {
        return field.FindNearestPoly(point, out polyRef, out position) && polyRef != 0;
    }

    public bool PathTo(Vector3 goal) {
        if (!TryFindPoly(agent.npos, out _, out _)) {
            return false;
        }

        if (!TryFindPoly(goal, out _, out RcVec3f end)) {
            return false;
        }

        return SetPathTo(end);
    }

    /// <summary>
    /// False when the navmesh does not connect the agent to <paramref name="goal"/>, e.g. a ledge it would drop down from.
    /// <paramref name="closest"/> is then the point nearest the goal that it can walk to.
    /// </summary>
    public bool CanWalkTo(Vector3 goal, out Vector3 closest) {
        closest = npc.Position;
        if (!TryFindPoly(agent.npos, out long startRef, out _) || !TryFindPoly(goal, out long goalRef, out RcVec3f goalPosition)) {
            return true; // off the mesh: leave it to PathTo
        }

        DtNavMeshQuery query = crowd.GetNavMeshQuery();
        List<long> polys = [];
        query.FindPath(startRef, goalRef, agent.npos, goalPosition, crowd.GetFilter(0), ref polys, new DtFindPathOption(0, float.MaxValue));
        if (polys.Count == 0) {
            return false;
        }
        if (polys[^1] == goalRef) {
            return true;
        }

        query.ClosestPointOnPoly(polys[^1], goalPosition, out RcVec3f end, out _);
        closest = DotRecastHelper.FromNavMeshSpace(end);
        return false;
    }

    private bool SetPathTo(RcVec3f target) {
        currentPath = [];
        currentPathIndex = 0;
        currentPathProgress = 0;
        try {
            currentPath = FindPath(agent.npos, target);
        } catch (Exception ex) {
            Logger.Error(ex, "Failed to find path to {Target}", target);
        }

        return currentPath is not null;
    }

    /// <summary>
    /// Paths to a point <paramref name="distance"/> away from <paramref name="goal"/>, on the far side from the goal, or as
    /// far toward it as the navmesh allows (walls and ledges stop the NPC).
    /// </summary>
    public bool PathAwayFrom(Vector3 goal, float distance) {
        if (!TryFindPoly(agent.npos, out long startRef, out RcVec3f position)) {
            return false;
        }

        // get target and distance in navmesh space
        RcVec3f target = DotRecastHelper.ToNavMeshSpace(goal);
        float fDistance = distance * DotRecastHelper.MapRotation.GetRightAxis().Length();

        if (!FindPointAway(crowd.GetNavMeshQuery(), crowd.GetFilter(0), startRef, position, target, fDistance, out RcVec3f reachable)) {
            return false;
        }

        if (!TryFindPoly(reachable, out _, out RcVec3f positionAwayNavMesh)) {
            return false;
        }

        return SetPathAway(positionAwayNavMesh);
    }

    /// <summary>Where a pull toward <paramref name="center"/> leaves the NPC: the center when the floor between is open, else
    /// where a wall or ledge stops it. False when it cannot move (already there, off the mesh, bad input).</summary>
    public bool PullToward(Vector3 center, out Vector3 destination) {
        destination = npc.Position;
        // From the NPC itself: the agent's position is synced only when a walk starts, so it can lag far behind.
        if (!TryFindPoly(npc.Position, out long startRef, out RcVec3f position)
            || !FindPullPoint(crowd.GetNavMeshQuery(), crowd.GetFilter(0), startRef, position, DotRecastHelper.ToNavMeshSpace(center), out RcVec3f reachable)
            // MoveAlongSurface keeps the start's height: put the end on the mesh (slopes, stacked floors).
            || !TryFindPoly(reachable, out _, out RcVec3f onMesh)) {
            return false;
        }
        destination = DotRecastHelper.FromNavMeshSpace(onMesh);
        return true;
    }

    public static bool FindPullPoint(DtNavMeshQuery query, IDtQueryFilter filter, long startRef, RcVec3f position, RcVec3f center, out RcVec3f reachable) {
        reachable = position;
        if (!position.IsFinite() || !center.IsFinite()) {
            return false;
        }
        // across the ground (Y is up in navmesh space); the surface stops the slide at walls and ledges
        var goal = new RcVec3f(center.X, position.Y, center.Z);
        Span<long> visited = stackalloc long[16];
        DtStatus status = query.MoveAlongSurface(startRef, position, goal, filter, out reachable, visited, out _, visited.Length);
        return status.Succeeded() && RcVec3f.Distance(position, reachable) >= DotRecastHelper.MIN_TARGET_DIST;
    }

    /// <summary>
    /// The point the agent at <paramref name="position"/> reaches running away from <paramref name="target"/> until it is
    /// <paramref name="distance"/> from it, all in navmesh space. False when it cannot move (cornered) or the input is bad.
    /// </summary>
    public static bool FindPointAway(DtNavMeshQuery query, IDtQueryFilter filter, long startRef, RcVec3f position, RcVec3f target, float distance, out RcVec3f reachable) {
        reachable = position;
        if (!float.IsFinite(distance) || distance <= 0 || !position.IsFinite() || !target.IsFinite()) {
            return false;
        }

        // flee across the ground (Y is up in navmesh space); standing on the target, any direction will do
        RcVec3f away = new RcVec3f(position.X - target.X, 0, position.Z - target.Z);
        float length = MathF.Sqrt(away.X * away.X + away.Z * away.Z);
        if (length < DotRecastHelper.MIN_TARGET_DIST) {
            float angle = Random.Shared.NextSingle() * MathF.PI * 2;
            away = new RcVec3f(MathF.Cos(angle), 0, MathF.Sin(angle));
        } else {
            away = away * (1 / length);
        }

        RcVec3f positionAway = new RcVec3f(target.X + away.X * distance, position.Y, target.Z + away.Z * distance);

        // the point itself is often off the navmesh (behind a wall, over a ledge): run as far toward it as the surface goes
        Span<long> visited = stackalloc long[16];
        DtStatus status = query.MoveAlongSurface(startRef, position, positionAway, filter, out reachable, visited, out _, visited.Length);
        return status.Succeeded() && RcVec3f.Distance(position, reachable) >= DotRecastHelper.MIN_TARGET_DIST;
    }

    private bool SetPathAway(RcVec3f target) {
        currentPath = [];
        currentPathIndex = 0;
        currentPathProgress = 0;
        try {
            currentPath = FindPath(agent.npos, target);
        } catch (Exception ex) {
            Logger.Error(ex, "Failed to find path away from {Target}", target);
        }

        return currentPath is not null;
    }
}
