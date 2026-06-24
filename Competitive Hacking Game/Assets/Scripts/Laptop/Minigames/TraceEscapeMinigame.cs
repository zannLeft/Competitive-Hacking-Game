using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class TraceEscapeMinigame : LaptopMinigameBase
{
    private const int CurrentTuningVersion = 20;

    [SerializeField, HideInInspector]
    private int tuningVersion;

    [Serializable]
    private sealed class TraceSettings
    {
        [Min(9)]
        public int mazeWidth = 25;

        [Min(9)]
        public int mazeHeight = 13;

        [Min(1)]
        public int accessKeyCount = 2;

        [Min(0f)]
        public float traceStartDelay = 2.8f;

        [Min(0.03f)]
        public float traceStepSeconds = 0.18f;

        [Min(0.03f)]
        public float movementRepeatSeconds = 0.105f;

        [Min(0f)]
        public float movementInitialRepeatDelay = 0.18f;

        [Range(0, 8)]
        public int visibleTraceTrailLength = 3;

        [Range(0, 24)]
        public int loopOpenings = 6;

        [Min(2)]
        public int maximumKeyDetour = 12;

        [Min(1)]
        public int minimumKeySpacing = 5;
    }

    private enum GameState
    {
        Playing,
        FailureHold,
        RoundTransition,
    }

    private enum CellVisualKind
    {
        None,
        Wall,
        Player,
        Trace,
        TraceTrail,
        Key,
        ExitLocked,
        ExitOpen,
    }

    private struct DeterministicRandom
    {
        private uint _state;

        public DeterministicRandom(int seed)
        {
            _state = seed == 0 ? 0xA341316Cu : unchecked((uint)seed);
        }

        public uint NextUInt()
        {
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
                return minInclusive;

            uint range = (uint)(maxExclusive - minInclusive);
            return minInclusive + (int)(NextUInt() % range);
        }

        public float Next01()
        {
            return (NextUInt() & 0x00FFFFFFu) / 16777216f;
        }
    }

    private readonly struct KeyCandidate
    {
        public KeyCandidate(Vector2Int cell, int startDistance, int detour)
        {
            Cell = cell;
            StartDistance = startDistance;
            Detour = detour;
        }

        public Vector2Int Cell { get; }
        public int StartDistance { get; }
        public int Detour { get; }
    }

    [Header("Difficulty")]
    [SerializeField]
    private TraceSettings easy = new()
    {
        mazeWidth = 25,
        mazeHeight = 17,
        accessKeyCount = 2,
        traceStartDelay = 2.45f,
        traceStepSeconds = 0.165f,
        movementRepeatSeconds = 0.105f,
        movementInitialRepeatDelay = 0.18f,
        visibleTraceTrailLength = 3,
        loopOpenings = 7,
        maximumKeyDetour = 12,
        minimumKeySpacing = 5,
    };

    [SerializeField]
    private TraceSettings hard = new()
    {
        mazeWidth = 31,
        mazeHeight = 19,
        accessKeyCount = 3,
        traceStartDelay = 2.05f,
        traceStepSeconds = 0.145f,
        movementRepeatSeconds = 0.09f,
        movementInitialRepeatDelay = 0.15f,
        visibleTraceTrailLength = 4,
        loopOpenings = 7,
        maximumKeyDetour = 16,
        minimumKeySpacing = 5,
    };

    [Header("Input")]
    [SerializeField, Range(0.05f, 0.95f)]
    private float navigationDeadzone = 0.35f;

    [Header("Series")]
    [Tooltip("Number of deterministic mazes that must be cleared before the router hack completes.")]
    [SerializeField, Range(1, 5)]
    private int roundsRequired = 3;

    [SerializeField, Min(0f)]
    private float roundTransitionSeconds = 0.75f;

    [Header("Result Timing")]
    [SerializeField, Min(0f)]
    private float failureHoldSeconds = 0.9f;

    [Header("ASCII Terminal Visuals")]
    [Tooltip("Assign the same monospace TMP font used by Firewall Runner.")]
    [SerializeField]
    private TMP_FontAsset terminalFont;

    [SerializeField]
    private Color backgroundColor = new(0.018f, 0.032f, 0.065f, 1f);

    [SerializeField]
    private Color accentColor = new(0.43f, 0.84f, 1f, 1f);

    [SerializeField]
    private Color textColor = new(0.91f, 0.97f, 1f, 1f);

    [SerializeField]
    private Color mutedTextColor = new(0.47f, 0.63f, 0.72f, 1f);

    [SerializeField]
    private Color dangerColor = new(1f, 0.24f, 0.38f, 1f);

    [SerializeField]
    private string playerGlyph = "@";

    [SerializeField]
    private string wallGlyph = "□";

    [SerializeField]
    private string keyGlyph = "K";

    [SerializeField]
    private string lockedExitGlyph = "X";

    [SerializeField]
    private string openExitGlyph = ">";

    [SerializeField]
    private string traceGlyph = "#";

    [SerializeField]
    private string traceTrailGlyph = ":";

    [Header("Layout")]
    [SerializeField, Range(2f, 20f)]
    private float separatorThickness = 8f;

    [SerializeField, Range(20f, 72f)]
    private float mazeMinimumFontSize = 28f;

    [SerializeField, Range(24f, 96f)]
    private float mazeMaximumFontSize = 60f;

    [SerializeField, Range(-30f, 30f)]
    private float mazeLineSpacing = 0f;

    [Tooltip("Forces every maze cell to use the same wider horizontal advance, keeping vertical corridors visually open and the terminal grid aligned.")]
    [SerializeField, Range(0.4f, 1.2f)]
    private float mazeCellWidthEm = 0.82f;

    [Tooltip("Forces a stable line height so block glyphs cannot visually spill into neighbouring maze rows.")]
    [SerializeField, Range(60f, 140f)]
    private float mazeLineHeightPercent = 75f;

    [Tooltip("Enlarges only the hollow wall glyph inside each fixed terminal cell so adjacent squares read more like continuous maze walls.")]
    [SerializeField, Range(100f, 175f)]
    private float wallGlyphScalePercent = 150f;

    private static readonly Vector2Int[] CardinalDirections =
    {
        Vector2Int.up,
        Vector2Int.right,
        Vector2Int.down,
        Vector2Int.left,
    };

    private readonly List<Vector2Int> _openCells = new();
    private readonly List<Vector2Int> _keyPositions = new();
    private readonly HashSet<Vector2Int> _remainingKeys = new();
    private readonly List<Vector2Int> _traceHistory = new();
    private readonly Queue<Vector2Int> _generationStack = new();
    private readonly Queue<Vector2Int> _cycleSearchQueue = new();
    private readonly HashSet<Vector2Int> _cycleVisited = new();
    private readonly Queue<Vector2Int> _traceSearchQueue = new();
    private readonly Dictionary<Vector2Int, Vector2Int> _traceParents = new();

    private RectTransform _root;
    private RectTransform _resultOverlayRect;
    private Image _resultOverlayImage;
    private TMP_Text _networkText;
    private TMP_Text _difficultyText;
    private TMP_Text _statusText;
    private TMP_Text _mazeText;
    private TMP_Text _resultText;

    private LaptopMinigameContext _context;
    private TraceSettings _settings;
    private bool[,] _walls;
    private Vector2Int _startPosition;
    private Vector2Int _exitPosition;
    private Vector2Int _playerPosition;
    private Vector2Int _tracePosition;
    private Vector2 _navigationInput;
    private Vector2Int _heldDirection;
    private float _elapsed;
    private float _traceStepTimer;
    private float _movementRepeatTimer;
    private float _failureTimer;
    private float _roundTransitionTimer;
    private int _currentRoundIndex;
    private bool _traceVisible;
    private bool _suppressMovementUntilNeutral;
    private GameState _state;

    private string _wallColorTag;
    private string _playerColorTag;
    private string _traceColorTag;
    private string _traceTrailColorTag;
    private string _keyColorTag;
    private string _lockedExitColorTag;
    private string _openExitColorTag;

    protected override void OnBegin(LaptopMinigameContext context)
    {
        EnsureDifficultySettingsExist();
        ApplyVersionedTuning();
        BuildUiIfNeeded();

        _context = context;
        _settings = context.Difficulty == LaptopMinigameDifficulty.Hard
            ? hard
            : easy;

        SanitizeSettings(_settings);
        _currentRoundIndex = 0;
        StartCurrentRound(requireNavigationRelease: false);
    }

    protected override void OnNavigationChanged(Vector2 input)
    {
        _navigationInput = Vector2.ClampMagnitude(input, 1f);

        Vector2Int direction = GetCardinalDirection(_navigationInput);

        if (direction == Vector2Int.zero)
        {
            _heldDirection = Vector2Int.zero;
            _movementRepeatTimer = 0f;
            _suppressMovementUntilNeutral = false;
            return;
        }

        if (_state != GameState.Playing || _suppressMovementUntilNeutral)
            return;

        if (direction == _heldDirection)
            return;

        _heldDirection = direction;
        _movementRepeatTimer = _settings.movementInitialRepeatDelay;
        TryMovePlayer(direction);
    }

    protected override void OnAbort()
    {
        _navigationInput = Vector2.zero;
        _heldDirection = Vector2Int.zero;
        _movementRepeatTimer = 0f;
        _roundTransitionTimer = 0f;
    }

    private void OnDisable()
    {
        if (IsRunning)
            Abort();
    }

    private void Update()
    {
        if (!IsRunning)
            return;

        float deltaTime = Mathf.Min(Time.deltaTime, 0.1f);

        if (_state == GameState.FailureHold)
        {
            _failureTimer -= deltaTime;

            if (_failureTimer <= 0f)
                ResetRun(requireNavigationRelease: true);

            return;
        }

        if (_state == GameState.RoundTransition)
        {
            _roundTransitionTimer -= deltaTime;

            if (_roundTransitionTimer <= 0f)
                StartCurrentRound(requireNavigationRelease: true);

            return;
        }

        _elapsed += deltaTime;
        UpdateHeldMovement(deltaTime);
        UpdateTrace(deltaTime);
    }

    private void UpdateHeldMovement(float deltaTime)
    {
        if (_suppressMovementUntilNeutral || _heldDirection == Vector2Int.zero)
            return;

        _movementRepeatTimer -= deltaTime;

        if (_movementRepeatTimer > 0f)
            return;

        _movementRepeatTimer += Mathf.Max(0.03f, _settings.movementRepeatSeconds);
        TryMovePlayer(_heldDirection);
    }

    private void UpdateTrace(float deltaTime)
    {
        if (_elapsed < _settings.traceStartDelay)
            return;

        _traceStepTimer -= deltaTime;

        while (_traceStepTimer <= 0f && _state == GameState.Playing)
        {
            _traceStepTimer += Mathf.Max(0.03f, _settings.traceStepSeconds);
            AdvanceTrace();
        }
    }

    private void AdvanceTrace()
    {
        if (_tracePosition == _playerPosition)
        {
            _traceVisible = true;
            BeginFailureHold();
            return;
        }

        Vector2Int next = FindNextShortestPathStep(
            _tracePosition,
            _playerPosition
        );

        if (next == _tracePosition)
            return;

        _tracePosition = next;
        _traceVisible = true;
        _traceHistory.Add(_tracePosition);

        int historyLimit = Mathf.Max(
            8,
            _settings.visibleTraceTrailLength + 6
        );

        if (_traceHistory.Count > historyLimit)
            _traceHistory.RemoveAt(0);

        if (_tracePosition == _playerPosition)
        {
            BeginFailureHold();
            return;
        }

        RefreshMazeText();
    }

    private Vector2Int FindNextShortestPathStep(
        Vector2Int origin,
        Vector2Int destination
    )
    {
        if (origin == destination)
            return origin;

        _traceSearchQueue.Clear();
        _traceParents.Clear();
        _traceSearchQueue.Enqueue(origin);
        _traceParents[origin] = origin;

        bool found = false;

        while (_traceSearchQueue.Count > 0 && !found)
        {
            Vector2Int current = _traceSearchQueue.Dequeue();

            for (int i = 0; i < CardinalDirections.Length; i++)
            {
                Vector2Int next = current + CardinalDirections[i];

                if (!IsOpen(next) || _traceParents.ContainsKey(next))
                    continue;

                _traceParents[next] = current;

                if (next == destination)
                {
                    found = true;
                    break;
                }

                _traceSearchQueue.Enqueue(next);
            }
        }

        if (!found)
            return origin;

        Vector2Int step = destination;

        while (
            _traceParents.TryGetValue(step, out Vector2Int parent)
            && parent != origin
            && parent != step
        )
        {
            step = parent;
        }

        return step;
    }

    private void TryMovePlayer(Vector2Int direction)
    {
        if (_state != GameState.Playing || direction == Vector2Int.zero)
            return;

        Vector2Int destination = _playerPosition + direction;

        if (!IsOpen(destination))
            return;

        _playerPosition = destination;

        // Each accepted grid step is a physical laptop keypress. The existing
        // PlayerLaptopHacker audio path plays it instantly for the owner and
        // relays it positionally to the other clients.
        TriggerActionPerformed();

        if (_remainingKeys.Remove(_playerPosition))
            UpdateStatusText();

        if (_traceVisible && _playerPosition == _tracePosition)
        {
            BeginFailureHold();
            return;
        }

        if (_playerPosition == _exitPosition && _remainingKeys.Count == 0)
        {
            CompleteCurrentRound();
            return;
        }

        RefreshMazeText();
    }

    private void BeginFailureHold()
    {
        if (_state != GameState.Playing)
            return;

        _state = GameState.FailureHold;
        _failureTimer = Mathf.Max(0f, failureHoldSeconds);
        _heldDirection = Vector2Int.zero;
        _movementRepeatTimer = 0f;
        TriggerAlarm();
        ShowResultOverlay(
            "!!! TRACE CAPTURED !!!\nRESTARTING LINK...",
            dangerColor
        );
    }

    private void CompleteCurrentRound()
    {
        if (_state != GameState.Playing)
            return;

        int completedRoundNumber = _currentRoundIndex + 1;

        if (completedRoundNumber >= roundsRequired)
        {
            CompleteMinigame();
            return;
        }

        _currentRoundIndex++;
        _state = GameState.RoundTransition;
        _roundTransitionTimer = Mathf.Max(0f, roundTransitionSeconds);
        _heldDirection = Vector2Int.zero;
        _movementRepeatTimer = 0f;
        _suppressMovementUntilNeutral = true;

        ShowResultOverlay(
            $"LINK {completedRoundNumber}/{roundsRequired} CLEARED\nROUTING NEXT NODE...",
            accentColor
        );
    }

    private void StartCurrentRound(bool requireNavigationRelease)
    {
        GenerateMaze(DeriveRoundSeed(_context.Seed, _currentRoundIndex));
        ResetRun(requireNavigationRelease);
    }

    private static int DeriveRoundSeed(int baseSeed, int roundIndex)
    {
        if (roundIndex <= 0)
            return baseSeed;

        unchecked
        {
            uint value = (uint)baseSeed;
            value ^= 0x9E3779B9u * (uint)(roundIndex + 1);
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            return (int)value;
        }
    }

    private void ResetRun(bool requireNavigationRelease)
    {
        _state = GameState.Playing;
        _playerPosition = _startPosition;
        _tracePosition = _startPosition;
        _traceVisible = false;
        _elapsed = 0f;
        _traceStepTimer = Mathf.Max(0.03f, _settings.traceStepSeconds);
        _movementRepeatTimer = 0f;
        _failureTimer = 0f;
        _roundTransitionTimer = 0f;
        _heldDirection = Vector2Int.zero;
        _suppressMovementUntilNeutral =
            requireNavigationRelease
            && _navigationInput.sqrMagnitude >= navigationDeadzone * navigationDeadzone;

        _traceHistory.Clear();
        _traceHistory.Add(_tracePosition);

        _remainingKeys.Clear();
        for (int i = 0; i < _keyPositions.Count; i++)
            _remainingKeys.Add(_keyPositions[i]);

        if (_networkText != null)
            _networkText.text = $"target={_context.NetworkDisplayName}";

        if (_difficultyText != null)
        {
            _difficultyText.text = $"[{_context.Difficulty.ToString().ToUpperInvariant()}]";
            _difficultyText.color = accentColor;
        }

        if (_resultOverlayRect != null)
            _resultOverlayRect.gameObject.SetActive(false);

        UpdateStatusText();
        RefreshMazeText();
    }

    private void GenerateMaze(int seed)
    {
        int width = MakeOdd(Mathf.Max(9, _settings.mazeWidth));
        int height = MakeOdd(Mathf.Max(9, _settings.mazeHeight));
        _walls = new bool[width, height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
                _walls[x, y] = true;
        }

        var random = new DeterministicRandom(seed);
        _startPosition = new Vector2Int(1, 1);
        CarvePerfectMaze(random, width, height);
        AddLoopOpenings(random, width, height, _settings.loopOpenings);
        RebuildOpenCellList(width, height);

        Dictionary<Vector2Int, int> distances = BuildDistances(_startPosition);
        _exitPosition = FindFarthestCell(distances, _startPosition);
        PlaceAccessKeys(random, distances, _settings.accessKeyCount);
        CacheColorTags();
    }

    private void CarvePerfectMaze(
        DeterministicRandom random,
        int width,
        int height
    )
    {
        var stack = new List<Vector2Int>(width * height / 4);
        Vector2Int current = _startPosition;
        _walls[current.x, current.y] = false;
        stack.Add(current);

        var candidates = new List<Vector2Int>(4);

        while (stack.Count > 0)
        {
            current = stack[stack.Count - 1];
            candidates.Clear();

            for (int i = 0; i < CardinalDirections.Length; i++)
            {
                Vector2Int candidate = current + CardinalDirections[i] * 2;

                if (
                    candidate.x <= 0
                    || candidate.y <= 0
                    || candidate.x >= width - 1
                    || candidate.y >= height - 1
                )
                {
                    continue;
                }

                if (_walls[candidate.x, candidate.y])
                    candidates.Add(candidate);
            }

            if (candidates.Count == 0)
            {
                stack.RemoveAt(stack.Count - 1);
                continue;
            }

            Vector2Int next = candidates[random.Range(0, candidates.Count)];
            Vector2Int between = (current + next) / 2;
            _walls[between.x, between.y] = false;
            _walls[next.x, next.y] = false;
            stack.Add(next);
        }
    }

    private void AddLoopOpenings(
        DeterministicRandom random,
        int width,
        int height,
        int requestedOpenings
    )
    {
        if (requestedOpenings <= 0)
            return;

        var candidates = new List<Vector2Int>();

        for (int y = 1; y < height - 1; y++)
        {
            for (int x = 1; x < width - 1; x++)
            {
                if (!_walls[x, y])
                    continue;

                bool horizontalConnector =
                    !_walls[x - 1, y]
                    && !_walls[x + 1, y]
                    && _walls[x, y - 1]
                    && _walls[x, y + 1];

                bool verticalConnector =
                    !_walls[x, y - 1]
                    && !_walls[x, y + 1]
                    && _walls[x - 1, y]
                    && _walls[x + 1, y];

                if (horizontalConnector || verticalConnector)
                    candidates.Add(new Vector2Int(x, y));
            }
        }

        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int swapIndex = random.Range(0, i + 1);
            (candidates[i], candidates[swapIndex]) =
                (candidates[swapIndex], candidates[i]);
        }

        int opened = 0;

        for (int i = 0; i < candidates.Count && opened < requestedOpenings; i++)
        {
            Vector2Int cell = candidates[i];

            if (!_walls[cell.x, cell.y])
                continue;

            bool horizontalConnector =
                !_walls[cell.x - 1, cell.y]
                && !_walls[cell.x + 1, cell.y];
            bool verticalConnector =
                !_walls[cell.x, cell.y - 1]
                && !_walls[cell.x, cell.y + 1];

            if (!horizontalConnector && !verticalConnector)
                continue;

            _walls[cell.x, cell.y] = false;
            opened++;
        }
    }

    private void RebuildOpenCellList(int width, int height)
    {
        _openCells.Clear();

        for (int y = 1; y < height - 1; y++)
        {
            for (int x = 1; x < width - 1; x++)
            {
                if (!_walls[x, y])
                    _openCells.Add(new Vector2Int(x, y));
            }
        }
    }

    private Dictionary<Vector2Int, int> BuildDistances(Vector2Int origin)
    {
        var distances = new Dictionary<Vector2Int, int>(_openCells.Count)
        {
            [origin] = 0,
        };

        _generationStack.Clear();
        _generationStack.Enqueue(origin);

        while (_generationStack.Count > 0)
        {
            Vector2Int current = _generationStack.Dequeue();
            int nextDistance = distances[current] + 1;

            for (int i = 0; i < CardinalDirections.Length; i++)
            {
                Vector2Int next = current + CardinalDirections[i];

                if (!IsOpen(next) || distances.ContainsKey(next))
                    continue;

                distances[next] = nextDistance;
                _generationStack.Enqueue(next);
            }
        }

        return distances;
    }

    private static Vector2Int FindFarthestCell(
        Dictionary<Vector2Int, int> distances,
        Vector2Int fallback
    )
    {
        Vector2Int farthest = fallback;
        int farthestDistance = -1;

        foreach (KeyValuePair<Vector2Int, int> pair in distances)
        {
            if (pair.Value <= farthestDistance)
                continue;

            farthestDistance = pair.Value;
            farthest = pair.Key;
        }

        return farthest;
    }

    private void PlaceAccessKeys(
        DeterministicRandom random,
        Dictionary<Vector2Int, int> distancesFromStart,
        int requestedCount
    )
    {
        _keyPositions.Clear();

        int availableCount = Mathf.Max(1, _openCells.Count - 2);
        int count = Mathf.Clamp(requestedCount, 1, availableCount);
        List<Vector2Int> solutionPath = BuildSolutionPath(distancesFromStart);
        var directPathCells = new HashSet<Vector2Int>(solutionPath);
        Dictionary<Vector2Int, int> distancesToExit = BuildDistances(_exitPosition);

        int directDistance = distancesFromStart.TryGetValue(
            _exitPosition,
            out int foundDirectDistance
        )
            ? foundDirectDistance
            : Mathf.Max(1, solutionPath.Count - 1);

        var candidates = new List<KeyCandidate>();
        CollectKeyCandidates(
            candidates,
            distancesFromStart,
            distancesToExit,
            directPathCells,
            directDistance,
            requireCycle: true,
            maximumDetour: _settings.maximumKeyDetour
        );

        // Small custom mazes may not contain enough loop cells. Expand the
        // search gradually while still preferring off-route, non-dead-end cells.
        if (candidates.Count < count)
        {
            CollectKeyCandidates(
                candidates,
                distancesFromStart,
                distancesToExit,
                directPathCells,
                directDistance,
                requireCycle: false,
                maximumDetour: _settings.maximumKeyDetour * 2
            );
        }

        candidates.Sort((a, b) =>
        {
            int distanceCompare = a.StartDistance.CompareTo(b.StartDistance);
            if (distanceCompare != 0)
                return distanceCompare;

            int detourCompare = a.Detour.CompareTo(b.Detour);
            if (detourCompare != 0)
                return detourCompare;

            int xCompare = a.Cell.x.CompareTo(b.Cell.x);
            return xCompare != 0 ? xCompare : a.Cell.y.CompareTo(b.Cell.y);
        });

        // Spread the keys outward through the maze. Each selected key is off the
        // direct route when possible, lies on a loop when possible, and adds only
        // a bounded detour. This makes exploration meaningful without requiring
        // an impossible retreat through a one-cell dead end.
        for (int keyIndex = 0; keyIndex < count && candidates.Count > 0; keyIndex++)
        {
            float targetFraction = (keyIndex + 1f) / (count + 1f);
            int targetDistance = Mathf.RoundToInt(directDistance * targetFraction);
            int selectedIndex = FindBestKeyCandidateIndex(
                candidates,
                targetDistance,
                _settings.minimumKeySpacing,
                ref random
            );

            if (selectedIndex < 0)
                selectedIndex = 0;

            _keyPositions.Add(candidates[selectedIndex].Cell);
            candidates.RemoveAt(selectedIndex);
        }

        // Final fallback: keep the game playable even with deliberately tiny or
        // heavily customized maze settings.
        for (int i = 0; _keyPositions.Count < count && i < solutionPath.Count; i++)
        {
            Vector2Int cell = solutionPath[i];

            if (
                cell == _startPosition
                || cell == _exitPosition
                || _keyPositions.Contains(cell)
                || !IsFarEnoughFromSelected(cell, 2)
            )
            {
                continue;
            }

            _keyPositions.Add(cell);
        }
    }

    private void CollectKeyCandidates(
        List<KeyCandidate> destination,
        Dictionary<Vector2Int, int> distancesFromStart,
        Dictionary<Vector2Int, int> distancesToExit,
        HashSet<Vector2Int> directPathCells,
        int directDistance,
        bool requireCycle,
        int maximumDetour
    )
    {
        var existing = new HashSet<Vector2Int>();

        for (int i = 0; i < destination.Count; i++)
            existing.Add(destination[i].Cell);

        for (int i = 0; i < _openCells.Count; i++)
        {
            Vector2Int cell = _openCells[i];

            if (
                cell == _startPosition
                || cell == _exitPosition
                || directPathCells.Contains(cell)
                || existing.Contains(cell)
                || CountOpenNeighbours(cell) < 2
                || !distancesFromStart.TryGetValue(cell, out int startDistance)
                || !distancesToExit.TryGetValue(cell, out int exitDistance)
                || startDistance < 4
                || exitDistance < 3
            )
            {
                continue;
            }

            int detour = startDistance + exitDistance - directDistance;

            if (detour < 1 || detour > maximumDetour)
                continue;

            if (requireCycle && !IsCellOnCycle(cell))
                continue;

            destination.Add(new KeyCandidate(cell, startDistance, detour));
            existing.Add(cell);
        }
    }

    private int FindBestKeyCandidateIndex(
        List<KeyCandidate> candidates,
        int targetDistance,
        int minimumSpacing,
        ref DeterministicRandom random
    )
    {
        int bestIndex = -1;
        float bestScore = float.PositiveInfinity;

        for (int i = 0; i < candidates.Count; i++)
        {
            KeyCandidate candidate = candidates[i];

            if (!IsFarEnoughFromSelected(candidate.Cell, minimumSpacing))
                continue;

            float score =
                Mathf.Abs(candidate.StartDistance - targetDistance)
                + candidate.Detour * 0.35f
                + random.Next01() * 0.75f;

            if (score >= bestScore)
                continue;

            bestScore = score;
            bestIndex = i;
        }

        if (bestIndex >= 0)
            return bestIndex;

        // Relax only the spacing requirement, never the reachability checks that
        // produced the candidate list.
        for (int i = 0; i < candidates.Count; i++)
        {
            KeyCandidate candidate = candidates[i];
            float score =
                Mathf.Abs(candidate.StartDistance - targetDistance)
                + candidate.Detour * 0.35f
                + random.Next01() * 0.75f;

            if (score >= bestScore)
                continue;

            bestScore = score;
            bestIndex = i;
        }

        return bestIndex;
    }

    private bool IsFarEnoughFromSelected(Vector2Int cell, int minimumSpacing)
    {
        int squaredMinimum = minimumSpacing * minimumSpacing;

        for (int i = 0; i < _keyPositions.Count; i++)
        {
            Vector2Int difference = _keyPositions[i] - cell;

            if (difference.sqrMagnitude < squaredMinimum)
                return false;
        }

        return true;
    }

    private bool IsCellOnCycle(Vector2Int cell)
    {
        var neighbours = new List<Vector2Int>(4);

        for (int i = 0; i < CardinalDirections.Length; i++)
        {
            Vector2Int neighbour = cell + CardinalDirections[i];

            if (IsOpen(neighbour))
                neighbours.Add(neighbour);
        }

        if (neighbours.Count < 2)
            return false;

        for (int first = 0; first < neighbours.Count - 1; first++)
        {
            for (int second = first + 1; second < neighbours.Count; second++)
            {
                if (AreConnectedWithoutCell(neighbours[first], neighbours[second], cell))
                    return true;
            }
        }

        return false;
    }

    private bool AreConnectedWithoutCell(
        Vector2Int origin,
        Vector2Int destination,
        Vector2Int blockedCell
    )
    {
        _cycleSearchQueue.Clear();
        _cycleVisited.Clear();
        _cycleSearchQueue.Enqueue(origin);
        _cycleVisited.Add(origin);

        while (_cycleSearchQueue.Count > 0)
        {
            Vector2Int current = _cycleSearchQueue.Dequeue();

            if (current == destination)
                return true;

            for (int i = 0; i < CardinalDirections.Length; i++)
            {
                Vector2Int next = current + CardinalDirections[i];

                if (
                    next == blockedCell
                    || !IsOpen(next)
                    || !_cycleVisited.Add(next)
                )
                {
                    continue;
                }

                _cycleSearchQueue.Enqueue(next);
            }
        }

        return false;
    }

    private List<Vector2Int> BuildSolutionPath(
        Dictionary<Vector2Int, int> distances
    )
    {
        var reversePath = new List<Vector2Int>();
        Vector2Int current = _exitPosition;
        reversePath.Add(current);

        while (current != _startPosition)
        {
            if (!distances.TryGetValue(current, out int currentDistance))
                break;

            bool foundPrevious = false;

            for (int i = 0; i < CardinalDirections.Length; i++)
            {
                Vector2Int previous = current + CardinalDirections[i];

                if (
                    distances.TryGetValue(previous, out int previousDistance)
                    && previousDistance == currentDistance - 1
                )
                {
                    current = previous;
                    reversePath.Add(current);
                    foundPrevious = true;
                    break;
                }
            }

            if (!foundPrevious)
                break;
        }

        reversePath.Reverse();
        return reversePath;
    }

    private int CountOpenNeighbours(Vector2Int cell)
    {
        int count = 0;

        for (int i = 0; i < CardinalDirections.Length; i++)
        {
            if (IsOpen(cell + CardinalDirections[i]))
                count++;
        }

        return count;
    }

    private Vector2Int GetCardinalDirection(Vector2 input)
    {
        if (input.sqrMagnitude < navigationDeadzone * navigationDeadzone)
            return Vector2Int.zero;

        if (Mathf.Abs(input.x) > Mathf.Abs(input.y))
            return input.x >= 0f ? Vector2Int.right : Vector2Int.left;

        return input.y >= 0f ? Vector2Int.up : Vector2Int.down;
    }

    private bool IsOpen(Vector2Int position)
    {
        if (_walls == null)
            return false;

        int width = _walls.GetLength(0);
        int height = _walls.GetLength(1);

        return position.x >= 0
            && position.y >= 0
            && position.x < width
            && position.y < height
            && !_walls[position.x, position.y];
    }

    private void UpdateStatusText()
    {
        if (_statusText == null)
            return;

        int total = _keyPositions.Count;
        int collected = total - _remainingKeys.Count;
        _statusText.text =
            $"[ROUND {_currentRoundIndex + 1}/{roundsRequired}] [KEYS {collected}/{total}]";
        _statusText.color = _remainingKeys.Count == 0 ? accentColor : textColor;
    }

    private void RefreshMazeText()
    {
        if (_mazeText == null || _walls == null)
            return;

        int width = _walls.GetLength(0);
        int height = _walls.GetLength(1);
        var builder = new StringBuilder(width * height * 8);
        CellVisualKind activeKind = CellVisualKind.None;

        // U+2588 can have different glyph metrics when TMP retrieves it from a
        // dynamic atlas or fallback. Force the entire board onto an explicit
        // terminal cell grid so walls, spaces, @, K, X and trace characters
        // always occupy exactly one logical column and one logical row.
        builder.Append("<mspace=");
        builder.Append(mazeCellWidthEm.ToString("0.###", CultureInfo.InvariantCulture));
        builder.Append("em><line-height=");
        builder.Append(mazeLineHeightPercent.ToString("0.###", CultureInfo.InvariantCulture));
        builder.Append("%>");

        for (int y = height - 1; y >= 0; y--)
        {
            for (int x = 0; x < width; x++)
            {
                Vector2Int cell = new(x, y);
                GetCellVisual(cell, out string glyph, out CellVisualKind kind);

                if (kind != activeKind)
                {
                    if (activeKind != CellVisualKind.None)
                        builder.Append("</color>");

                    builder.Append(GetColorTag(kind));
                    activeKind = kind;
                }

                if (kind == CellVisualKind.Wall && glyph != " ")
                {
                    builder.Append("<size=");
                    builder.Append(
                        wallGlyphScalePercent.ToString(
                            "0.###",
                            CultureInfo.InvariantCulture
                        )
                    );
                    builder.Append("%>");
                    builder.Append(glyph);
                    builder.Append("</size>");
                }
                else
                {
                    builder.Append(glyph);
                }
            }

            if (activeKind != CellVisualKind.None)
            {
                builder.Append("</color>");
                activeKind = CellVisualKind.None;
            }

            if (y > 0)
                builder.Append('\n');
        }

        builder.Append("</line-height></mspace>");
        _mazeText.text = builder.ToString();
    }

    private void GetCellVisual(
        Vector2Int cell,
        out string glyph,
        out CellVisualKind kind
    )
    {
        if (cell == _playerPosition)
        {
            glyph = playerGlyph;
            kind = CellVisualKind.Player;
            return;
        }

        if (_traceVisible && cell == _tracePosition)
        {
            glyph = traceGlyph;
            kind = CellVisualKind.Trace;
            return;
        }

        if (IsTraceTrailCell(cell))
        {
            glyph = traceTrailGlyph;
            kind = CellVisualKind.TraceTrail;
            return;
        }

        if (_remainingKeys.Contains(cell))
        {
            glyph = keyGlyph;
            kind = CellVisualKind.Key;
            return;
        }

        if (cell == _exitPosition)
        {
            bool unlocked = _remainingKeys.Count == 0;
            glyph = unlocked ? openExitGlyph : lockedExitGlyph;
            kind = unlocked ? CellVisualKind.ExitOpen : CellVisualKind.ExitLocked;
            return;
        }

        if (_walls[cell.x, cell.y])
        {
            glyph = wallGlyph;
            kind = CellVisualKind.Wall;
            return;
        }

        glyph = " ";
        kind = CellVisualKind.Wall;
    }

    private bool IsTraceTrailCell(Vector2Int cell)
    {
        if (
            !_traceVisible
            || _settings.visibleTraceTrailLength <= 0
            || _traceHistory.Count <= 1
        )
        {
            return false;
        }

        int lastExclusive = _traceHistory.Count - 1;
        int firstIndex = Mathf.Max(
            0,
            lastExclusive - _settings.visibleTraceTrailLength
        );

        for (int i = firstIndex; i < lastExclusive; i++)
        {
            if (_traceHistory[i] == cell)
                return true;
        }

        return false;
    }

    private string GetColorTag(CellVisualKind kind)
    {
        return kind switch
        {
            CellVisualKind.Player => _playerColorTag,
            CellVisualKind.Trace => _traceColorTag,
            CellVisualKind.TraceTrail => _traceTrailColorTag,
            CellVisualKind.Key => _keyColorTag,
            CellVisualKind.ExitLocked => _lockedExitColorTag,
            CellVisualKind.ExitOpen => _openExitColorTag,
            _ => _wallColorTag,
        };
    }

    private void CacheColorTags()
    {
        _wallColorTag = BuildColorTag(mutedTextColor);
        _playerColorTag = BuildColorTag(accentColor);
        _traceColorTag = BuildColorTag(dangerColor);
        _traceTrailColorTag = BuildColorTag(WithAlpha(dangerColor, 0.58f));
        _keyColorTag = BuildColorTag(textColor);
        _lockedExitColorTag = BuildColorTag(WithAlpha(mutedTextColor, 0.72f));
        _openExitColorTag = BuildColorTag(accentColor);
    }

    private static string BuildColorTag(Color color)
    {
        return $"<color=#{ColorUtility.ToHtmlStringRGBA(color)}>";
    }

    private void BuildUiIfNeeded()
    {
        if (_root != null)
            return;

        _root = (RectTransform)transform;
        StretchToParent(_root);

        Image background = CreateImage("Background", _root, backgroundColor);
        Stretch(background.rectTransform, Vector2.zero, Vector2.one);

        TMP_Text title = CreateText(
            "Title",
            _root,
            "> TRACE_ESCAPE",
            58f,
            TextAlignmentOptions.MidlineLeft,
            textColor,
            FontStyles.Bold
        );
        Stretch(
            title.rectTransform,
            new Vector2(0.050f, 0.920f),
            new Vector2(0.66f, 0.975f)
        );
        title.overflowMode = TextOverflowModes.Overflow;

        _difficultyText = CreateText(
            "Difficulty",
            _root,
            "[EASY]",
            38f,
            TextAlignmentOptions.MidlineRight,
            accentColor,
            FontStyles.Bold
        );
        Stretch(
            _difficultyText.rectTransform,
            new Vector2(0.69f, 0.920f),
            new Vector2(0.950f, 0.975f)
        );
        _difficultyText.overflowMode = TextOverflowModes.Overflow;

        _networkText = CreateText(
            "Network",
            _root,
            "target=unknown",
            36f,
            TextAlignmentOptions.MidlineLeft,
            mutedTextColor,
            FontStyles.Normal
        );
        Stretch(
            _networkText.rectTransform,
            new Vector2(0.050f, 0.865f),
            new Vector2(0.62f, 0.920f)
        );

        _statusText = CreateText(
            "Status",
            _root,
            "[KEYS 0/0]",
            35f,
            TextAlignmentOptions.MidlineRight,
            textColor,
            FontStyles.Bold
        );
        Stretch(
            _statusText.rectTransform,
            new Vector2(0.54f, 0.865f),
            new Vector2(0.950f, 0.920f)
        );

        CreateDashSeparator("HeaderDivider", 0.847f);

        RectTransform gameArea = CreateRect("GameArea", _root);
        Stretch(
            gameArea,
            new Vector2(0.020f, 0.118f),
            new Vector2(0.980f, 0.835f)
        );

        _mazeText = CreateText(
            "Maze",
            gameArea,
            string.Empty,
            mazeMaximumFontSize,
            TextAlignmentOptions.Center,
            mutedTextColor,
            FontStyles.Normal
        );
        Stretch(
            _mazeText.rectTransform,
            new Vector2(0.015f, 0.015f),
            new Vector2(0.985f, 0.985f)
        );
        _mazeText.enableAutoSizing = true;
        _mazeText.fontSizeMin = mazeMinimumFontSize;
        _mazeText.fontSizeMax = mazeMaximumFontSize;
        _mazeText.lineSpacing = mazeLineSpacing;
        _mazeText.characterSpacing = 0f;
        _mazeText.overflowMode = TextOverflowModes.Masking;
        _mazeText.textWrappingMode = TextWrappingModes.NoWrap;
        _mazeText.richText = true;

        CreateDashSeparator("FooterDivider", 0.103f);

        TMP_Text instruction = CreateText(
            "Instruction",
            _root,
            "[WASD] MOVE          COLLECT K -> X          [Q] DISCONNECT",
            35f,
            TextAlignmentOptions.Center,
            mutedTextColor,
            FontStyles.Bold
        );
        Stretch(
            instruction.rectTransform,
            new Vector2(0.025f, 0.018f),
            new Vector2(0.975f, 0.086f)
        );

        _resultOverlayImage = CreateImage(
            "ResultOverlay",
            _root,
            new Color(backgroundColor.r, backgroundColor.g, backgroundColor.b, 0.96f)
        );
        _resultOverlayRect = _resultOverlayImage.rectTransform;
        Stretch(_resultOverlayRect, Vector2.zero, Vector2.one);

        _resultText = CreateText(
            "ResultText",
            _resultOverlayRect,
            string.Empty,
            78f,
            TextAlignmentOptions.Center,
            dangerColor,
            FontStyles.Bold
        );
        Stretch(_resultText.rectTransform, Vector2.zero, Vector2.one);
        _resultOverlayRect.gameObject.SetActive(false);
    }

    private void ShowResultOverlay(string message, Color color)
    {
        if (_resultOverlayRect == null)
            return;

        _resultOverlayRect.gameObject.SetActive(true);

        if (_resultOverlayImage != null)
        {
            _resultOverlayImage.color = new Color(
                backgroundColor.r,
                backgroundColor.g,
                backgroundColor.b,
                0.96f
            );
        }

        if (_resultText != null)
        {
            _resultText.text = message;
            _resultText.color = color;
        }
    }

    private RectTransform CreateRect(string objectName, Transform parent)
    {
        var gameObject = new GameObject(objectName, typeof(RectTransform));
        var rect = gameObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private Image CreateImage(string objectName, Transform parent, Color color)
    {
        var gameObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image)
        );
        var rect = gameObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);

        Image image = gameObject.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private TMP_Text CreateText(
        string objectName,
        Transform parent,
        string value,
        float fontSize,
        TextAlignmentOptions alignment,
        Color color,
        FontStyles fontStyle
    )
    {
        var gameObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI)
        );
        var rect = gameObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);

        var text = gameObject.GetComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = color;
        text.fontStyle = fontStyle;
        text.raycastTarget = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.characterSpacing = 1f;

        if (terminalFont != null)
            text.font = terminalFont;
        else if (TMP_Settings.defaultFontAsset != null)
            text.font = TMP_Settings.defaultFontAsset;

        return text;
    }

    private TMP_Text CreateDashSeparator(string objectName, float normalizedY)
    {
        string dividerText = new string('-', 320);
        Color dividerColor = WithAlpha(mutedTextColor, 0.92f);
        float dividerFontSize = 36f;
        float verticalOffset = Mathf.Max(1.5f, separatorThickness * 0.22f);

        TMP_Text lower = CreateText(
            $"{objectName}_Lower",
            _root,
            dividerText,
            dividerFontSize,
            TextAlignmentOptions.Center,
            dividerColor,
            FontStyles.Bold
        );
        ConfigureDashSeparatorRect(lower.rectTransform, normalizedY, -verticalOffset);
        lower.characterSpacing = -1.5f;
        lower.overflowMode = TextOverflowModes.Masking;
        lower.textWrappingMode = TextWrappingModes.NoWrap;

        TMP_Text upper = CreateText(
            objectName,
            _root,
            dividerText,
            dividerFontSize,
            TextAlignmentOptions.Center,
            dividerColor,
            FontStyles.Bold
        );
        ConfigureDashSeparatorRect(upper.rectTransform, normalizedY, verticalOffset);
        upper.characterSpacing = -1.5f;
        upper.overflowMode = TextOverflowModes.Masking;
        upper.textWrappingMode = TextWrappingModes.NoWrap;
        return upper;
    }

    private void ConfigureDashSeparatorRect(
        RectTransform rect,
        float normalizedY,
        float verticalOffset
    )
    {
        rect.anchorMin = new Vector2(0f, normalizedY);
        rect.anchorMax = new Vector2(1f, normalizedY);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(
            0f,
            Mathf.Max(54f, separatorThickness * 7f)
        );
        rect.anchoredPosition = new Vector2(0f, verticalOffset);
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    private static Color WithAlpha(Color color, float alphaMultiplier)
    {
        color.a = Mathf.Clamp01(color.a * alphaMultiplier);
        return color;
    }

    private static void StretchToParent(RectTransform rect)
    {
        if (rect == null)
            return;

        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    private static void Stretch(
        RectTransform rect,
        Vector2 anchorMin,
        Vector2 anchorMax
    )
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    private static int MakeOdd(int value)
    {
        return (value & 1) == 0 ? value + 1 : value;
    }

    private static void SanitizeSettings(TraceSettings settings)
    {
        if (settings == null)
            return;

        settings.mazeWidth = Mathf.Clamp(MakeOdd(Mathf.Max(9, settings.mazeWidth)), 9, 45);
        settings.mazeHeight = Mathf.Clamp(MakeOdd(Mathf.Max(9, settings.mazeHeight)), 9, 25);
        settings.accessKeyCount = Mathf.Max(1, settings.accessKeyCount);
        settings.traceStartDelay = Mathf.Max(0f, settings.traceStartDelay);
        settings.traceStepSeconds = Mathf.Max(0.03f, settings.traceStepSeconds);
        settings.movementRepeatSeconds = Mathf.Max(0.03f, settings.movementRepeatSeconds);
        settings.movementInitialRepeatDelay = Mathf.Max(0f, settings.movementInitialRepeatDelay);
        settings.visibleTraceTrailLength = Mathf.Clamp(settings.visibleTraceTrailLength, 0, 8);
        settings.loopOpenings = Mathf.Clamp(settings.loopOpenings, 0, 24);
        settings.maximumKeyDetour = Mathf.Max(2, settings.maximumKeyDetour);
        settings.minimumKeySpacing = Mathf.Max(1, settings.minimumKeySpacing);
    }

    private void EnsureDifficultySettingsExist()
    {
        easy ??= new TraceSettings
        {
            mazeWidth = 25,
            mazeHeight = 17,
            accessKeyCount = 2,
            traceStartDelay = 2.45f,
            traceStepSeconds = 0.165f,
            movementRepeatSeconds = 0.105f,
            movementInitialRepeatDelay = 0.18f,
            visibleTraceTrailLength = 3,
            loopOpenings = 7,
            maximumKeyDetour = 12,
            minimumKeySpacing = 5,
        };

        hard ??= new TraceSettings
        {
            mazeWidth = 31,
            mazeHeight = 19,
            accessKeyCount = 3,
            traceStartDelay = 2.05f,
            traceStepSeconds = 0.145f,
            movementRepeatSeconds = 0.09f,
            movementInitialRepeatDelay = 0.15f,
            visibleTraceTrailLength = 4,
            loopOpenings = 7,
            maximumKeyDetour = 16,
            minimumKeySpacing = 5,
        };
    }

    private void ApplyVersionedTuning()
    {
        if (tuningVersion >= CurrentTuningVersion)
            return;

        // Preserve custom Inspector tuning: only migrate values that still match
        // the original Trace Escape release defaults.
        if (hard != null)
        {
            if (Mathf.Approximately(hard.traceStartDelay, 1.9f))
                hard.traceStartDelay = 2.35f;

            if (Mathf.Approximately(hard.traceStepSeconds, 0.135f))
                hard.traceStepSeconds = 0.16f;
        }

        // Iosevka Term includes both solid and hollow square terminal glyphs.
        // The hollow square keeps the maze readable without filling the screen
        // with one solid block of accent color.
        if (string.IsNullOrEmpty(wallGlyph) || wallGlyph == "#")
            wallGlyph = "□";

        if (tuningVersion < 2)
        {
            // The original negative spacing looked fine with '#', but a full
            // block fills the complete glyph box and visibly overlapped the
            // rows above and below. Preserve custom values and only migrate the
            // original release value.
            if (Mathf.Approximately(mazeLineSpacing, -8f))
                mazeLineSpacing = 0f;

            if (mazeCellWidthEm <= 0f)
                mazeCellWidthEm = 0.62f;

            if (mazeLineHeightPercent <= 0f)
                mazeLineHeightPercent = 100f;
        }

        if (tuningVersion < 3)
        {
            // The first aligned version used the natural narrow monospace
            // advance. That made one-cell vertical corridors look pinched
            // compared with horizontal corridors. A wider cell advance makes
            // each logical maze tile much closer to square.
            if (Mathf.Approximately(mazeCellWidthEm, 0.62f))
                mazeCellWidthEm = 0.82f;
        }

        if (tuningVersion < 4)
        {
            // Make the pursuing trace easier to identify at a glance while
            // preserving any custom glyph chosen in the Inspector.
            if (string.IsNullOrEmpty(traceGlyph) || traceGlyph == "!")
                traceGlyph = "#";

            // Now that keys are placed along the guaranteed route, both
            // difficulties can apply slightly more pressure without forcing
            // unfair dead-end backtracking. Only migrate the previous defaults.
            if (easy != null)
            {
                if (Mathf.Approximately(easy.traceStartDelay, 2.8f))
                    easy.traceStartDelay = 2.55f;

                if (Mathf.Approximately(easy.traceStepSeconds, 0.18f))
                    easy.traceStepSeconds = 0.17f;
            }

            if (hard != null)
            {
                if (Mathf.Approximately(hard.traceStartDelay, 2.35f))
                    hard.traceStartDelay = 2.15f;

                if (Mathf.Approximately(hard.traceStepSeconds, 0.16f))
                    hard.traceStepSeconds = 0.15f;
            }
        }

        if (tuningVersion < 5)
        {
            // The terminal cell advance is intentionally wider than a normal
            // monospace glyph. The first pass reduced row height, but that made
            // horizontal wall rows visually crowd the corridor between them.
            if (Mathf.Approximately(mazeLineHeightPercent, 100f))
                mazeLineHeightPercent = 92f;
        }

        if (tuningVersion < 6)
        {
            // Give each logical row enough vertical advance that an empty cell
            // reads close to the same size as the 0.82em horizontal cell.
            // Auto-sizing keeps the complete maze inside the gameplay region.
            if (Mathf.Approximately(mazeLineHeightPercent, 92f))
                mazeLineHeightPercent = 112f;
        }

        if (tuningVersion < 7)
        {
            // The 112% pass made vertical corridors slightly more open than
            // horizontal corridors. Pull the row advance back just enough to
            // make the logical cells read square without returning to overlap.
            if (Mathf.Approximately(mazeLineHeightPercent, 112f))
                mazeLineHeightPercent = 106f;
        }

        if (tuningVersion < 8)
        {
            // Trace Escape is now a three-link breach. Existing prefabs may
            // deserialize the newly added field as zero, so migrate only that
            // unconfigured state and preserve deliberate custom values.
            if (roundsRequired <= 1)
                roundsRequired = 3;

            if (roundTransitionSeconds <= 0f)
                roundTransitionSeconds = 0.75f;
        }

        if (tuningVersion < 9)
        {
            // The security trace now actively hunts the packet by taking one
            // breadth-first-search step along the current shortest route every
            // trace tick. The existing timing values remain unchanged; the
            // smarter pursuit itself is the difficulty increase.
        }

        if (tuningVersion < 12)
        {
            // V12 removes all TMP glow and underlay halo. The trace now uses
            // the exact same hazard red as Firewall Runner obstacles.
            dangerColor = new Color(1f, 0.24f, 0.38f, 1f);
        }

        if (tuningVersion < 13)
        {
            // V13 adds only a small amount of extra pressure to the existing
            // shortest-path pursuer. Preserve deliberate custom values and
            // migrate only the prior defaults.
            if (easy != null)
            {
                if (Mathf.Approximately(easy.traceStartDelay, 2.55f))
                    easy.traceStartDelay = 2.45f;

                if (Mathf.Approximately(easy.traceStepSeconds, 0.17f))
                    easy.traceStepSeconds = 0.165f;
            }

            if (hard != null)
            {
                if (Mathf.Approximately(hard.traceStartDelay, 2.15f))
                    hard.traceStartDelay = 2.05f;

                if (Mathf.Approximately(hard.traceStepSeconds, 0.15f))
                    hard.traceStepSeconds = 0.145f;
            }
        }

        if (tuningVersion < 14)
        {
            // V14 adds looped mazes and bounded side-route key placement so the
            // objectives require deliberate detours without becoming impossible.
            if (wallGlyph == "█")
                wallGlyph = "□";

            if (easy != null)
            {
                if (easy.loopOpenings <= 0)
                    easy.loopOpenings = 7;
                if (easy.maximumKeyDetour <= 0)
                    easy.maximumKeyDetour = 12;
                if (easy.minimumKeySpacing <= 0)
                    easy.minimumKeySpacing = 5;
            }

            if (hard != null)
            {
                if (hard.loopOpenings <= 0)
                    hard.loopOpenings = 7;
                if (hard.maximumKeyDetour <= 0)
                    hard.maximumKeyDetour = 16;
                if (hard.minimumKeySpacing <= 0)
                    hard.minimumKeySpacing = 5;
            }
        }

        if (tuningVersion < 15)
        {
            // Keep the new looped maze and safe detour generation, but restore
            // the more aggressive security daemon that recomputes the shortest
            // route to the player on every trace tick. Slightly enlarge only the
            // hollow wall glyph so the cells almost join without changing the
            // logical maze spacing or collision grid.
            if (wallGlyphScalePercent <= 100f)
                wallGlyphScalePercent = 116f;
        }


        if (tuningVersion < 16)
        {
            // V16 makes the hollow wall squares substantially larger inside the
            // same logical cells. The grid and collisions do not change, but the
            // □ glyphs nearly meet so corridors read as solid terminal walls.
            if (wallGlyphScalePercent <= 116f)
                wallGlyphScalePercent = 150f;
        }


        if (tuningVersion < 17)
        {
            // The hollow square is much shorter than a normal terminal line.
            // Reducing the row advance makes the logical cells genuinely square
            // instead of leaving large vertical gaps. The extra vertical room is
            // spent on taller odd-sized mazes, keeping the board's on-screen
            // height close to the previous version while adding more routes.
            if (Mathf.Approximately(mazeLineHeightPercent, 106f))
                mazeLineHeightPercent = 84f;

            if (easy != null && easy.mazeHeight == 13)
                easy.mazeHeight = 17;

            if (hard != null && hard.mazeHeight == 15)
                hard.mazeHeight = 19;
        }

        if (tuningVersion < 18)
        {
            // V18 pulls the remaining row pitch in slightly so hollow cells read
            // as an even grid. Walls return to the muted terminal color, leaving
            // the player and open exit as the stronger accent elements.
            if (Mathf.Approximately(mazeLineHeightPercent, 84f))
                mazeLineHeightPercent = 80f;
        }

        if (tuningVersion < 19)
        {
            // V19 makes the final small row-pitch correction requested after the
            // taller mazes were introduced. Horizontal cell width is unchanged.
            if (Mathf.Approximately(mazeLineHeightPercent, 80f))
                mazeLineHeightPercent = 77f;
        }

        if (tuningVersion < 20)
        {
            // V20 makes the final two-percent row-pitch adjustment so the hollow
            // wall grid reads as evenly spaced vertically and horizontally.
            if (Mathf.Approximately(mazeLineHeightPercent, 77f))
                mazeLineHeightPercent = 75f;
        }

        tuningVersion = CurrentTuningVersion;
    }

    private void OnValidate()
    {
        EnsureDifficultySettingsExist();
        ApplyVersionedTuning();
        SanitizeSettings(easy);
        SanitizeSettings(hard);
        navigationDeadzone = Mathf.Clamp(navigationDeadzone, 0.05f, 0.95f);
        roundsRequired = Mathf.Clamp(roundsRequired, 1, 5);
        roundTransitionSeconds = Mathf.Max(0f, roundTransitionSeconds);
        failureHoldSeconds = Mathf.Max(0f, failureHoldSeconds);
        separatorThickness = Mathf.Clamp(separatorThickness, 2f, 20f);
        mazeMinimumFontSize = Mathf.Clamp(mazeMinimumFontSize, 20f, 72f);
        mazeMaximumFontSize = Mathf.Max(mazeMinimumFontSize, mazeMaximumFontSize);
        mazeLineSpacing = Mathf.Clamp(mazeLineSpacing, -30f, 30f);
        mazeCellWidthEm = Mathf.Clamp(mazeCellWidthEm, 0.4f, 1.2f);
        mazeLineHeightPercent = Mathf.Clamp(mazeLineHeightPercent, 60f, 140f);
        wallGlyphScalePercent = Mathf.Clamp(wallGlyphScalePercent, 100f, 175f);

        if (string.IsNullOrEmpty(playerGlyph))
            playerGlyph = "@";
        if (string.IsNullOrEmpty(wallGlyph))
            wallGlyph = "□";
        if (string.IsNullOrEmpty(keyGlyph))
            keyGlyph = "K";
        if (string.IsNullOrEmpty(lockedExitGlyph))
            lockedExitGlyph = "X";
        if (string.IsNullOrEmpty(openExitGlyph))
            openExitGlyph = ">";
        if (string.IsNullOrEmpty(traceGlyph))
            traceGlyph = "#";
        if (string.IsNullOrEmpty(traceTrailGlyph))
            traceTrailGlyph = ":";
    }
}
