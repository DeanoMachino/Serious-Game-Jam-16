﻿using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Pathfinding;

/** Simple movement script.
 * This movement script will follow the path exactly, it uses linear interpolation to move between the waypoints in the path.
 * This is desirable for some types of games.
 * It also works in 2D.
 *
 * For nicer movement, I recommend adding the Simple Smooth Modifier to the GameObject as well.
 *
 * \ingroup movementscripts
 */
[RequireComponent(typeof(Seeker))]
[AddComponentMenu("Pathfinding/AI/AISimpleLerp (2D,3D generic)")]
public class AISimpleLerp : MonoBehaviour {

    /** Determines how often it will search for new paths.
     * If you have fast moving targets or AIs, you might want to set it to a lower value.
     * The value is in seconds between path requests.
     */
    public float repathRate = 0.5F;

    /** Target to move towards.
     * The AI will try to follow/move towards this target.
     * It can be a point on the ground where the player has clicked in an RTS for example, or it can be the player object in a zombie game.
     */
    public Transform target;

    /** Enables or disables searching for paths.
     * Setting this to false does not stop any active path requests from being calculated or stop it from continuing to follow the current path.
     * \see #canMove
     */
    public bool canSearch = true;

    /** Enables or disables movement.
      * \see #canSearch */
    public bool canMove = true;

    /** Speed in world units */
    public float speed = 3;

    /** If true, the AI will rotate to face the movement direction */
    public bool enableRotation = true;

    /** If true, rotation will only be done along the Z axis */
    public bool rotationIn2D = false;

    /** How quickly to rotate */
    public float rotationSpeed = 10;

    /** If true, some interpolation will be done when a new path has been calculated.
     * This is used to avoid short distance teleportation.
     */
    public bool interpolatePathSwitches = true;

    /** How quickly to interpolate to the new path */
    public float switchPathInterpolationSpeed = 5;

    /** Cached Seeker component */
    protected Seeker seeker;

    /** Cached Transform component */
    protected Transform tr;

    /** Time when the last path request was sent */
    protected float lastRepath = -9999;

    /** Current path which is followed */
    protected ABPath path;

    /** Current index in the path which is current target */
    protected int currentWaypointIndex = 0;

    /** How far the AI has moved on the current segment */
    protected float lerpTime = 0;

    /** Holds if the end-of-path is reached
     * \see TargetReached */
    protected bool targetReached = false;

    /** Only when the previous path has been returned should be search for a new path */
    protected bool canSearchAgain = true;

    /** When a new path was returned, the AI was moving along this ray.
     * Used to smoothly interpolate between the previous movement and the movement along the new path.
     * The speed is equal to movement direction.
     */
    protected Vector3 previousMovementOrigin;
    protected Vector3 previousMovementDirection;
    protected float previousMovementStartTime = -9999;

    /** Returns if the end-of-path has been reached
     * \see targetReached */
    public bool TargetReached {
        get {
            return targetReached;
        }
    }

    /** Holds if the Start function has been run.
     * Used to test if coroutines should be started in OnEnable to prevent calculating paths
     * in the awake stage (or rather before start on frame 0).
     */
    private bool startHasRun = false;

    /** Initializes reference variables.
     * If you override this function you should in most cases call base.Awake () at the start of it.
      * */
    protected virtual void Awake() {
        seeker = GetComponent<Seeker>();

        //This is a simple optimization, cache the transform component lookup
        tr = transform;
    }

    /** Starts searching for paths.
     * If you override this function you should in most cases call base.Start () at the start of it.
     * \see OnEnable
     * \see RepeatTrySearchPath
     */
    protected virtual void Start() {
        startHasRun = true;
        OnEnable();
    }

    /** Run at start and when reenabled.
     * Starts RepeatTrySearchPath.
     *
     * \see Start
     */
    protected virtual void OnEnable() {

        lastRepath = -9999;
        canSearchAgain = true;

        if (startHasRun) {
            //Make sure we receive callbacks when paths complete
            seeker.pathCallback += OnPathComplete;

            StartCoroutine(RepeatTrySearchPath());
        }
    }

    public void OnDisable() {
        // Abort calculation of path
        if (seeker != null && !seeker.IsDone())
            seeker.GetCurrentPath().Error();

        // Release current path
        if (path != null)
            path.Release(this);
        path = null;

        //Make sure we receive callbacks when paths complete
        seeker.pathCallback -= OnPathComplete;
    }

    /** Tries to search for a path every #repathRate seconds.
      * \see TrySearchPath
      */
    protected IEnumerator RepeatTrySearchPath() {
        while (true) {
            float v = TrySearchPath();
            yield return new WaitForSeconds(v);
        }
    }

    /** Tries to search for a path.
     * Will search for a new path if there was a sufficient time since the last repath and both
     * #canSearchAgain and #canSearch are true and there is a target.
     *
     * \returns The time to wait until calling this function again (based on #repathRate)
     */
    public float TrySearchPath() {
        if (Time.time - lastRepath >= repathRate && canSearchAgain && canSearch && target != null) {
            SearchPath();
            return repathRate;
        } else {
            float v = repathRate - (Time.time - lastRepath);
            return v < 0 ? 0 : v;
        }
    }

    /** Requests a path to the target.
     * Some inheriting classes will prevent the path from being requested immediately when
     * this function is called, for example when the AI is currently traversing a special path segment
     * in which case it is usually a bad idea to search for a new path.
      */
    public virtual void SearchPath() {
        ForceSearchPath();
    }

    /** Requests a path to the target.
     * Bypasses 'is-it-a-good-time-to-request-a-path' checks.
      */
    public virtual void ForceSearchPath() {
        if (target == null)
            throw new System.InvalidOperationException("Target is null");

        lastRepath = Time.time;
        //This is where we should search to
        Vector3 targetPosition = target.position;

        canSearchAgain = false;

        //Alternative way of requesting the path
        //ABPath p = ABPath.Construct (GetFeetPosition(),targetPoint,null);
        //seeker.StartPath (p);

        //We should search from the current position
        seeker.StartPath(GetFeetPosition(), targetPosition);
    }

    /** The end of the path has been reached.
      * If you want custom logic for when the AI has reached it's destination
      * add it here
      * You can also create a new script which inherits from this one
      * and override the function in that script.
      */
    public virtual void OnTargetReached() {

    }

    /** Called when a requested path has finished calculation.
      * A path is first requested by #SearchPath, it is then calculated, probably in the same or the next frame.
      * Finally it is returned to the seeker which forwards it to this function.\n
      */
    public virtual void OnPathComplete(Path _p) {
        ABPath p = _p as ABPath;
        if (p == null)
            throw new System.Exception("This function only handles ABPaths, do not use special path types");

        canSearchAgain = true;

        //Claim the new path
        p.Claim(this);

        // Path couldn't be calculated of some reason.
        // More info in p.errorLog (debug string)
        if (p.error) {
            p.Release(this);
            return;
        }

        if (interpolatePathSwitches) {
            ConfigurePathSwitchInterpolation();
        }

        //Release the previous path
        if (path != null)
            path.Release(this);

        //Replace the old path
        path = p;

        // Just for the rest of the code to work, if there is only one waypoint in the path
        // add another one
        if (path.vectorPath != null && path.vectorPath.Count == 1) {
            path.vectorPath.Insert(0, GetFeetPosition());
        }

        targetReached = false;

        //Reset some variables
        ConfigureNewPath();
    }

    protected virtual void ConfigurePathSwitchInterpolation() {
        if (path != null && path.vectorPath != null && path.vectorPath.Count > 1) {

            List<Vector3> vPath = path.vectorPath;

            // Make sure we stay inside valid ranges
            currentWaypointIndex = Mathf.Clamp(currentWaypointIndex, 1, vPath.Count - 1);

            // Current segment vector
            Vector3 segment = vPath[currentWaypointIndex] - vPath[currentWaypointIndex - 1];
            float segmentLength = segment.magnitude;

            // Find the approximate length of the path that is left on the current path
            float approximateLengthLeft = segmentLength * Mathf.Clamp01(1 - lerpTime);
            for (int i = currentWaypointIndex; i < vPath.Count - 1; i++) {
                approximateLengthLeft += (vPath[i + 1] - vPath[i]).magnitude;
            }

            previousMovementOrigin = GetFeetPosition();
            previousMovementDirection = segment.normalized * approximateLengthLeft;
            previousMovementStartTime = Time.time;
        } else {
            previousMovementOrigin = Vector3.zero;
            previousMovementDirection = Vector3.zero;
            previousMovementStartTime = -9999;
        }
    }

    public virtual Vector3 GetFeetPosition() {
        return tr.position;
    }

    /** Finds the closest point on the current path.
     * Sets #currentWaypointIndex and #lerpTime to the appropriate values.
     */
    protected virtual void ConfigureNewPath() {
        var points = path.vectorPath;

        var currentPosition = GetFeetPosition();

        float bestFactor = 0;
        float bestDist = float.PositiveInfinity;
        int bestIndex = 0;

        for (int i = 0; i < points.Count - 1; i++) {
            float factor = AstarMath.NearestPointFactor(points[i], points[i + 1], currentPosition);
            Vector3 point = Vector3.Lerp(points[i], points[i + 1], factor);
            float dist = (currentPosition - point).sqrMagnitude;

            if (dist < bestDist) {
                bestDist = dist;
                bestFactor = factor;
                bestIndex = i + 1;
            }
        }

        currentWaypointIndex = bestIndex;
        lerpTime = bestFactor;
    }

    protected virtual void Update() {
        if (canMove) {
            Vector3 direction;
            Vector3 nextPos = CalculateNextPosition(out direction);

            // Rotate unless we are really close to the target
            if (enableRotation && direction != Vector3.zero) {

                if (rotationIn2D) {

                    float angle = Mathf.Atan2(-direction.x, direction.y) * Mathf.Rad2Deg + 180;
                    Vector3 euler = tr.eulerAngles;
                    euler.z = Mathf.LerpAngle(euler.z, angle, Time.deltaTime * rotationSpeed);
                    tr.eulerAngles = euler;
                } else {

                    Quaternion rot = tr.rotation;
                    Quaternion desiredRot = Quaternion.LookRotation(direction);

                    tr.rotation = Quaternion.Slerp(rot, desiredRot, Time.deltaTime * rotationSpeed);
                }
            }

            tr.position = nextPos;
        }
    }

    /** Calculate the AI's next position (one frame in the future).
     * \param direction The direction of the segment the AI is currently traversing. Not normalized.
     */
    protected virtual Vector3 CalculateNextPosition(out Vector3 direction) {

        if (path == null || path.vectorPath == null || path.vectorPath.Count == 0) {
            direction = Vector3.zero;
            return Vector3.zero;
        }

        List<Vector3> vPath = path.vectorPath;

        // Make sure we stay inside valid ranges
        currentWaypointIndex = Mathf.Clamp(currentWaypointIndex, 1, vPath.Count - 1);

        // Current segment vector
        Vector3 segment = vPath[currentWaypointIndex] - vPath[currentWaypointIndex - 1];
        float segmentLength = segment.magnitude;

        if (segmentLength > 0) {
            // Move forwards
            // lerpTime is between 0 and 1
            lerpTime += Time.deltaTime * speed / segmentLength;
        } else {
            // Make sure zero length segments are handled correctly
            lerpTime = 1;
        }

        // Pick the next segment if we have traversed the current one completely
        if (lerpTime > 1 && currentWaypointIndex < vPath.Count - 1) {
            float overshootDistance = (lerpTime - 1) * segmentLength;

            while (true) {
                currentWaypointIndex++;

                // Next segment vector
                Vector3 nextSegment = vPath[currentWaypointIndex] - vPath[currentWaypointIndex - 1];
                float nextSegmentLength = segment.magnitude;

                if (overshootDistance <= nextSegmentLength || currentWaypointIndex == vPath.Count - 1) {
                    segment = nextSegment;
                    segmentLength = nextSegmentLength;
                    lerpTime = Mathf.Clamp01(overshootDistance / nextSegmentLength);
                    break;
                } else {
                    overshootDistance -= nextSegmentLength;
                }
            }
        }

        if (lerpTime >= 1 && currentWaypointIndex == vPath.Count - 1) {
            if (!targetReached) {
                OnTargetReached();
            }
            targetReached = true;
        }

        // Find our position along the path using a simple linear interpolation
        Vector3 positionAlongCurrentPath = segment * Mathf.Clamp01(lerpTime) + vPath[currentWaypointIndex - 1];

        direction = segment;

        if (interpolatePathSwitches) {
            // Find the approximate position we would be at if we
            // would have continued to follow the previous path
            Vector3 positionAlongPreviousPath = previousMovementOrigin + Vector3.ClampMagnitude(previousMovementDirection, speed * (Time.time - previousMovementStartTime));

            // Use this to debug
            //Debug.DrawLine (previousMovementOrigin, positionAlongPreviousPath, Color.yellow);


            return Vector3.Lerp(positionAlongPreviousPath, positionAlongCurrentPath, switchPathInterpolationSpeed * (Time.time - previousMovementStartTime));
        } else {
            return positionAlongCurrentPath;
        }
    }
}