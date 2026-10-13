using System.Collections.ObjectModel;
using System.Text;

namespace ChessAtlas.Domain.Chess;

/// <summary>
/// A mutable chess position with a full undo stack: legal move generation, castling, en passant,
/// promotion, check/mate/stalemate detection, FEN I/O and Zobrist keys.
///
/// <para>
/// The class is intentionally not immutable. Undo/redo, engine analysis and perft all walk the same
/// game many times, and make/unmake on one instance is the only design that keeps those walks cheap.
/// Callers that need a stable picture call <see cref="Clone"/>. The invariant that matters is
/// narrower than immutability: <b>every mutation goes through <see cref="MakeMove"/> or
/// <see cref="UndoMove"/></b>, so a <see cref="Position"/> can never describe an illegal board.
/// </para>
/// </summary>
public sealed class Position
{
    public const string StandardFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    private static readonly (int df, int dr)[] KnightJumps =
    {
        (1, 2), (2, 1), (2, -1), (1, -2), (-1, -2), (-2, -1), (-2, 1), (-1, 2),
    };

    private static readonly (int df, int dr)[] KingSteps =
    {
        (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1),
    };

    private static readonly (int df, int dr)[] Diagonals = { (1, 1), (1, -1), (-1, 1), (-1, -1) };
    private static readonly (int df, int dr)[] Orthogonals = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    private readonly Piece?[] _board = new Piece?[Square.Count];
    private readonly List<UndoInfo> _history = new();

    private Position()
    {
    }

    public Side SideToMove { get; private set; }
    public CastlingRights CastlingRights { get; private set; }

    /// <summary>
    /// The square an en-passant capture would land on, or <c>null</c>. Recorded for every double
    /// pawn push, following the FEN convention — it does not by itself promise a legal capture.
    /// </summary>
    public Square? EnPassantTarget { get; private set; }

    public int HalfmoveClock { get; private set; }
    public int FullmoveNumber { get; private set; }

    /// <summary>Incrementally maintained Zobrist key of the current position.</summary>
    public ulong ZobristKey { get; private set; }

    /// <summary>Number of half-moves played on this instance.</summary>
    public int PlyCount => _history.Count;

    public static Position StartPosition => FromFen(StandardFen);

    public static Position Empty() => new();

    public Piece? At(Square square) => _board[square.Index];

    public bool IsEmpty(Square square) => !_board[square.Index].HasValue;

    public bool IsOccupied(Square square) => _board[square.Index].HasValue;

    public IReadOnlyList<Move> History => ReadOnlyCollectionFor(_history);

    public Position Clone()
    {
        var copy = new Position();
        Array.Copy(_board, copy._board, Square.Count);
        copy.SideToMove = SideToMove;
        copy.CastlingRights = CastlingRights;
        copy.EnPassantTarget = EnPassantTarget;
        copy.HalfmoveClock = HalfmoveClock;
        copy.FullmoveNumber = FullmoveNumber;
        copy.ZobristKey = ZobristKey;
        copy._history.AddRange(_history);
        return copy;
    }

    private static IReadOnlyList<Move> ReadOnlyCollectionFor(List<UndoInfo> history)
    {
        var moves = new Move[history.Count];
        for (var i = 0; i < history.Count; i++) moves[i] = history[i].Move;
        return new ReadOnlyCollection<Move>(moves);
    }

    // --------------------------------------------------------------------------------------
    // FEN
    // --------------------------------------------------------------------------------------

    public static Position FromFen(string fen)
    {
        if (string.IsNullOrWhiteSpace(fen))
            throw new FormatException("FEN string is empty.");

        var fields = fen.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 4)
            throw new FormatException($"FEN '{fen}' has {fields.Length} fields; at least 4 are required.");

        var position = new Position();
        position.PlacePiecesFromFen(fields[0]);

        position.SideToMove = fields[1].Trim().ToLowerInvariant() switch
        {
            "w" => Side.White,
            "b" => Side.Black,
            _ => throw new FormatException($"FEN side-to-move field '{fields[1]}' is neither 'w' nor 'b'."),
        };

        position.CastlingRights = CastlingRightsExtensions.ParseFenField(fields[2]);
        position.EnPassantTarget = fields[3] == "-" ? null : Square.Parse(fields[3]);

        position.HalfmoveClock = fields.Length > 4 && int.TryParse(fields[4], out var half) ? Math.Max(0, half) : 0;
        position.FullmoveNumber = fields.Length > 5 && int.TryParse(fields[5], out var full) ? Math.Max(1, full) : 1;

        position.ZobristKey = position.ComputeZobrist();
        return position;
    }

    private void PlacePiecesFromFen(string placement)
    {
        Array.Clear(_board, 0, _board.Length);

        var ranks = placement.Split('/');
        if (ranks.Length != 8)
            throw new FormatException($"FEN placement '{placement}' must describe exactly 8 ranks.");

        for (var i = 0; i < 8; i++)
        {
            var rank = 7 - i; // FEN lists rank 8 first.
            var file = 0;
            foreach (var c in ranks[i])
            {
                if (char.IsDigit(c))
                {
                    file += c - '0';
                    continue;
                }

                if (!Piece.TryParseFenChar(c, out var piece))
                    throw new FormatException($"'{c}' is not a valid FEN piece character.");
                if (file > 7)
                    throw new FormatException($"FEN rank '{ranks[i]}' is too wide.");

                _board[(rank << 3) | file] = piece;
                file++;
            }

            if (file != 8)
                throw new FormatException($"FEN rank '{ranks[i]}' does not cover 8 files.");
        }

        if (CountKings(Side.White) != 1) throw new FormatException("FEN must contain exactly one white king.");
        if (CountKings(Side.Black) != 1) throw new FormatException("FEN must contain exactly one black king.");
    }

    private int CountKings(Side side)
    {
        var count = 0;
        for (var i = 0; i < Square.Count; i++)
            if (_board[i] is Piece p && p.Side == side && p.Type == PieceType.King) count++;
        return count;
    }

    public string ToFen()
    {
        var sb = new StringBuilder(96);

        for (var rank = 7; rank >= 0; rank--)
        {
            var empty = 0;
            for (var file = 0; file < 8; file++)
            {
                if (_board[(rank << 3) | file] is not Piece piece)
                {
                    empty++;
                    continue;
                }

                if (empty > 0)
                {
                    sb.Append(empty);
                    empty = 0;
                }

                sb.Append(piece.ToFenChar());
            }

            if (empty > 0) sb.Append(empty);
            if (rank > 0) sb.Append('/');
        }

        sb.Append(' ').Append(SideToMove == Side.White ? 'w' : 'b');
        sb.Append(' ').Append(CastlingRights.ToFenField());
        sb.Append(' ').Append(EnPassantTarget?.ToString() ?? "-");
        sb.Append(' ').Append(HalfmoveClock);
        sb.Append(' ').Append(FullmoveNumber);
        return sb.ToString();
    }

    /// <summary>
    /// The identity of the position for transposition purposes: piece placement, side to move and
    /// castling rights, plus an en-passant file <em>only when an en-passant capture is actually
    /// legal</em>. The half-move and full-move counters are excluded on purpose: they are
    /// bookkeeping, not position.
    /// </summary>
    public string ToPositionKey()
    {
        var fen = ToFen();
        var fields = fen.Split(' ');
        var ep = "-";
        if (EnPassantTarget is Square target && HasLegalEnPassantCapture()) ep = target.ToString();
        return $"{fields[0]} {fields[1]} {fields[2]} {ep}";
    }

    private bool HasLegalEnPassantCapture()
    {
        if (EnPassantTarget is not Square target) return false;

        var us = SideToMove;
        var pawnRank = us == Side.White ? target.Rank - 1 : target.Rank + 1;
        foreach (var df in new[] { -1, 1 })
        {
            var file = target.File + df;
            if ((uint)file > 7) continue;
            if (_board[(pawnRank << 3) | file] is not Piece p) continue;
            if (p.Side != us || p.Type != PieceType.Pawn) continue;

            var move = new Move(Square.FromFileRank(file, pawnRank), target);
            MakeMove(move);
            var kingSafe = !IsSquareAttackedBy(FindKing(us), us.Other());
            UndoMove();
            if (kingSafe) return true;
        }

        return false;
    }

    // --------------------------------------------------------------------------------------
    // Attacks
    // --------------------------------------------------------------------------------------

    public bool IsSquareAttackedBy(Square target, Side attacker)
    {
        // Pawns attack one rank "forward" from their own side, so an attacking white pawn sits
        // one rank *below* the target.
        var pawnRankDelta = attacker == Side.White ? -1 : 1;
        foreach (var df in new[] { -1, 1 })
        {
            if (!target.IsInside(df, pawnRankDelta)) continue;
            if (_board[target.Offset(df, pawnRankDelta).Index] is Piece pawn &&
                pawn.Side == attacker && pawn.Type == PieceType.Pawn) return true;
        }

        foreach (var (df, dr) in KnightJumps)
        {
            if (!target.IsInside(df, dr)) continue;
            if (_board[target.Offset(df, dr).Index] is Piece knight &&
                knight.Side == attacker && knight.Type == PieceType.Knight) return true;
        }

        foreach (var (df, dr) in KingSteps)
        {
            if (!target.IsInside(df, dr)) continue;
            if (_board[target.Offset(df, dr).Index] is Piece king &&
                king.Side == attacker && king.Type == PieceType.King) return true;
        }

        foreach (var (df, dr) in Diagonals)
        {
            var file = target.File + df;
            var rank = target.Rank + dr;
            while ((uint)file <= 7 && (uint)rank <= 7)
            {
                if (_board[(rank << 3) | file] is Piece piece)
                {
                    if (piece.Side == attacker && piece.Type is PieceType.Bishop or PieceType.Queen) return true;
                    break;
                }

                file += df;
                rank += dr;
            }
        }

        foreach (var (df, dr) in Orthogonals)
        {
            var file = target.File + df;
            var rank = target.Rank + dr;
            while ((uint)file <= 7 && (uint)rank <= 7)
            {
                if (_board[(rank << 3) | file] is Piece piece)
                {
                    if (piece.Side == attacker && piece.Type is PieceType.Rook or PieceType.Queen) return true;
                    break;
                }

                file += df;
                rank += dr;
            }
        }

        return false;
    }

    public Square FindKing(Side side)
    {
        for (var i = 0; i < Square.Count; i++)
            if (_board[i] is Piece piece && piece.Side == side && piece.Type == PieceType.King)
                return Square.FromIndex(i);

        throw new InvalidOperationException($"No {side} king on the board.");
    }

    public bool IsSideInCheck(Side side) => IsSquareAttackedBy(FindKing(side), side.Other());

    public bool IsInCheck => IsSideInCheck(SideToMove);

    // --------------------------------------------------------------------------------------
    // Move generation
    // --------------------------------------------------------------------------------------

    public IReadOnlyList<Move> GeneratePseudoLegalMoves()
    {
        var us = SideToMove;
        var moves = new List<Move>(48);

        for (var index = 0; index < Square.Count; index++)
        {
            if (_board[index] is not Piece piece || piece.Side != us) continue;
            var from = Square.FromIndex(index);

            switch (piece.Type)
            {
                case PieceType.Pawn:
                    GeneratePawnMoves(from, us, moves);
                    break;
                case PieceType.Knight:
                    GenerateJumpMoves(from, us, KnightJumps, moves);
                    break;
                case PieceType.King:
                    GenerateJumpMoves(from, us, KingSteps, moves);
                    GenerateCastlingMoves(us, moves);
                    break;
                case PieceType.Bishop:
                    GenerateSlideMoves(from, us, Diagonals, moves);
                    break;
                case PieceType.Rook:
                    GenerateSlideMoves(from, us, Orthogonals, moves);
                    break;
                case PieceType.Queen:
                    GenerateSlideMoves(from, us, Diagonals, moves);
                    GenerateSlideMoves(from, us, Orthogonals, moves);
                    break;
            }
        }

        return moves;
    }

    private void GeneratePawnMoves(Square from, Side us, List<Move> moves)
    {
        var forward = us == Side.White ? 1 : -1;
        var startRank = us == Side.White ? 1 : 6;
        var promotionRank = us == Side.White ? 7 : 0;

        if (from.IsInside(0, forward))
        {
            var one = from.Offset(0, forward);
            if (IsEmpty(one))
            {
                AddPawnMove(moves, from, one, promotionRank);

                if (from.Rank == startRank)
                {
                    var two = one.Offset(0, forward);
                    if (IsEmpty(two)) moves.Add(new Move(from, two));
                }
            }
        }

        foreach (var df in new[] { -1, 1 })
        {
            if (!from.IsInside(df, forward)) continue;
            var to = from.Offset(df, forward);

            if (EnPassantTarget is Square ep && to == ep && IsEmpty(to))
            {
                moves.Add(new Move(from, to));
                continue;
            }

            if (_board[to.Index] is Piece target && target.Side != us)
                AddPawnMove(moves, from, to, promotionRank);
        }
    }

    private static void AddPawnMove(List<Move> moves, Square from, Square to, int promotionRank)
    {
        if (to.Rank == promotionRank)
        {
            moves.Add(new Move(from, to, PieceType.Queen));
            moves.Add(new Move(from, to, PieceType.Rook));
            moves.Add(new Move(from, to, PieceType.Bishop));
            moves.Add(new Move(from, to, PieceType.Knight));
        }
        else
        {
            moves.Add(new Move(from, to));
        }
    }

    private void GenerateJumpMoves(Square from, Side us, (int df, int dr)[] offsets, List<Move> moves)
    {
        foreach (var (df, dr) in offsets)
        {
            if (!from.IsInside(df, dr)) continue;
            var to = from.Offset(df, dr);
            if (_board[to.Index] is Piece occupant && occupant.Side == us) continue;
            moves.Add(new Move(from, to));
        }
    }

    private void GenerateSlideMoves(Square from, Side us, (int df, int dr)[] directions, List<Move> moves)
    {
        foreach (var (df, dr) in directions)
        {
            var file = from.File + df;
            var rank = from.Rank + dr;
            while ((uint)file <= 7 && (uint)rank <= 7)
            {
                var to = Square.FromFileRank(file, rank);
                if (_board[to.Index] is Piece occupant)
                {
                    if (occupant.Side != us) moves.Add(new Move(from, to));
                    break;
                }

                moves.Add(new Move(from, to));
                file += df;
                rank += dr;
            }
        }
    }

    private void GenerateCastlingMoves(Side us, List<Move> moves)
    {
        var rank = us == Side.White ? 0 : 7;
        var enemy = us.Other();
        var kingSide = us == Side.White ? CastlingRights.WhiteKingSide : CastlingRights.BlackKingSide;
        var queenSide = us == Side.White ? CastlingRights.WhiteQueenSide : CastlingRights.BlackQueenSide;
        var rook = Piece.Of(us, PieceType.Rook);

        if (CastlingRights.Has(kingSide))
        {
            var king = Square.FromFileRank(4, rank);
            var f = Square.FromFileRank(5, rank);
            var g = Square.FromFileRank(6, rank);
            var h = Square.FromFileRank(7, rank);

            if (_board[king.Index] is Piece k && k.Type == PieceType.King &&
                _board[h.Index] is Piece rh && rh == rook &&
                IsEmpty(f) && IsEmpty(g) &&
                !IsSquareAttackedBy(king, enemy) &&
                !IsSquareAttackedBy(f, enemy) &&
                !IsSquareAttackedBy(g, enemy))
            {
                moves.Add(new Move(king, g));
            }
        }

        if (CastlingRights.Has(queenSide))
        {
            var king = Square.FromFileRank(4, rank);
            var d = Square.FromFileRank(3, rank);
            var c = Square.FromFileRank(2, rank);
            var b = Square.FromFileRank(1, rank);
            var a = Square.FromFileRank(0, rank);

            if (_board[king.Index] is Piece k && k.Type == PieceType.King &&
                _board[a.Index] is Piece ra && ra == rook &&
                IsEmpty(b) && IsEmpty(c) && IsEmpty(d) &&
                !IsSquareAttackedBy(king, enemy) &&
                !IsSquareAttackedBy(d, enemy) &&
                !IsSquareAttackedBy(c, enemy))
            {
                moves.Add(new Move(king, c));
            }
        }
    }

    /// <summary>Every move the side to move may legally play, in a deterministic order.</summary>
    public IReadOnlyList<Move> GenerateLegalMoves()
    {
        var us = SideToMove;
        var pseudo = GeneratePseudoLegalMoves();
        var legal = new List<Move>(pseudo.Count);

        foreach (var move in pseudo)
        {
            MakeMove(move);
            if (!IsSquareAttackedBy(FindKing(us), us.Other())) legal.Add(move);
            UndoMove();
        }

        return legal;
    }

    public bool IsLegal(Move move)
    {
        if (_board[move.From.Index] is not Piece piece || piece.Side != SideToMove) return false;

        foreach (var candidate in GenerateLegalMoves())
            if (candidate == move) return true;

        return false;
    }

    // --------------------------------------------------------------------------------------
    // Make / unmake
    // --------------------------------------------------------------------------------------

    /// <summary>
    /// Plays a move after proving it is legal. This is the entry point for user input, PGN replay
    /// and the trainer; <see cref="MakeMove"/> alone trusts its caller.
    /// </summary>
    public void PlayMove(Move move)
    {
        var found = false;
        foreach (var legal in GenerateLegalMoves())
        {
            if (legal != move) continue;
            found = true;
            break;
        }

        if (!found) throw new IllegalMoveException(move, "the move is not in the legal move list.");
        MakeMove(move);
    }

    /// <summary>True when <paramref name="move"/> is among the legal moves from this position.</summary>
    public bool TryPlayMove(Move move)
    {
        if (!IsLegal(move)) return false;
        MakeMove(move);
        return true;
    }

    /// <summary>
    /// Applies a move, assuming it came from <see cref="GeneratePseudoLegalMoves"/> or was validated
    /// by <see cref="IsLegal"/>. Pseudo-legal generation needs a fast path; everything that faces
    /// a human goes through <see cref="PlayMove"/> instead.
    /// </summary>
    public void MakeMove(Move move)
    {
        var us = SideToMove;
        var fromIndex = move.From.Index;

        if (_board[fromIndex] is not Piece piece)
            throw new IllegalMoveException(move, "the origin square is empty.");
        if (piece.Side != us)
            throw new IllegalMoveException(move, "it is not that side's turn.");

        var toIndex = move.To.Index;
        var capturedSquare = (Square?)move.To;
        var captured = _board[toIndex];
        var isEnPassant = false;

        if (piece.Type == PieceType.Pawn && move.From.File != move.To.File && captured is null)
        {
            if (EnPassantTarget is not Square ep || move.To != ep)
                throw new IllegalMoveException(move, "a pawn cannot move diagonally onto an empty square.");

            capturedSquare = Square.FromFileRank(move.To.File, move.From.Rank);
            captured = _board[capturedSquare.Value.Index];
            if (captured is not Piece epPawn || epPawn.Side == us || epPawn.Type != PieceType.Pawn)
                throw new IllegalMoveException(move, "no pawn is present to capture en passant.");
            isEnPassant = true;
        }

        if (captured is not null && move.From == move.To)
            throw new IllegalMoveException(move, "a piece cannot capture itself.");

        var isCastling = piece.Type == PieceType.King && Math.Abs(move.To.File - move.From.File) == 2;
        if (isCastling)
        {
            var rookSquare = move.To.File == 6
                ? Square.FromFileRank(7, move.From.Rank)
                : Square.FromFileRank(0, move.From.Rank);
            if (_board[rookSquare.Index] is not Piece rook || rook.Side != us || rook.Type != PieceType.Rook)
                throw new IllegalMoveException(move, "castling requires a rook on its home square.");
        }

        var promotionRank = us == Side.White ? 7 : 0;
        if (move.IsPromotion && (piece.Type != PieceType.Pawn || move.To.Rank != promotionRank))
            throw new IllegalMoveException(move, "only a pawn reaching the last rank may promote.");

        _history.Add(new UndoInfo(
            move,
            captured,
            capturedSquare,
            CastlingRights,
            EnPassantTarget,
            HalfmoveClock,
            FullmoveNumber,
            ZobristKey));

        if (captured is not null) _board[capturedSquare!.Value.Index] = null;
        _board[fromIndex] = null;
        _board[toIndex] = move.IsPromotion ? Piece.Of(us, move.Promotion) : piece;

        if (isCastling)
        {
            var rank = move.From.Rank;
            if (move.To.File == 6)
            {
                _board[(rank << 3) | 5] = _board[(rank << 3) | 7];
                _board[(rank << 3) | 7] = null;
            }
            else
            {
                _board[(rank << 3) | 3] = _board[(rank << 3) | 0];
                _board[(rank << 3) | 0] = null;
            }
        }

        CastlingRights &= CastlingRightsExtensions.MaskFor(move.From) & CastlingRightsExtensions.MaskFor(move.To);

        EnPassantTarget = piece.Type == PieceType.Pawn && Math.Abs(move.To.Rank - move.From.Rank) == 2
            ? Square.FromFileRank(move.From.File, (move.From.Rank + move.To.Rank) / 2)
            : null;

        HalfmoveClock = piece.Type == PieceType.Pawn || captured is not null || isEnPassant
            ? 0
            : HalfmoveClock + 1;

        if (us == Side.Black) FullmoveNumber++;
        SideToMove = us.Other();
        ZobristKey = ComputeZobrist();
    }

    public void UndoMove()
    {
        if (_history.Count == 0)
            throw new InvalidOperationException("There is no move to undo.");

        var undo = _history[^1];
        _history.RemoveAt(_history.Count - 1);

        var move = undo.Move;
        var us = SideToMove.Other(); // the side that played the move being undone
        var fromIndex = move.From.Index;
        var toIndex = move.To.Index;

        var piece = _board[toIndex] ?? throw new InvalidOperationException("Corrupt position: destination is empty.");
        var restored = move.IsPromotion ? Piece.Of(us, PieceType.Pawn) : piece;
        _board[fromIndex] = restored;
        _board[toIndex] = null;

        if (undo.Captured is Piece captured) _board[undo.CapturedSquare!.Value.Index] = captured;

        var isCastling = restored.Type == PieceType.King && Math.Abs(move.To.File - move.From.File) == 2;
        if (isCastling)
        {
            var rank = move.From.Rank;
            if (move.To.File == 6)
            {
                _board[(rank << 3) | 7] = _board[(rank << 3) | 5];
                _board[(rank << 3) | 5] = null;
            }
            else
            {
                _board[(rank << 3) | 0] = _board[(rank << 3) | 3];
                _board[(rank << 3) | 3] = null;
            }
        }

        CastlingRights = undo.Castling;
        EnPassantTarget = undo.EnPassantTarget;
        HalfmoveClock = undo.HalfmoveClock;
        FullmoveNumber = undo.FullmoveNumber;
        SideToMove = us;
        ZobristKey = undo.Zobrist;
    }

    // --------------------------------------------------------------------------------------
    // Status
    // --------------------------------------------------------------------------------------

    /// <summary>
    /// Terminal status of the position. Threefold repetition cannot be detected from a position
    /// alone, so it is only reported when the caller supplies the Zobrist history of the game.
    /// </summary>
    public GameStatus EvaluateStatus(IReadOnlyList<ulong>? zobristHistory = null)
    {
        // Mate and stalemate end the game on the spot, so they outrank every draw rule.
        if (GenerateLegalMoves().Count == 0)
            return IsInCheck ? GameStatus.Checkmate : GameStatus.Stalemate;

        if (HasInsufficientMaterial()) return GameStatus.DrawByInsufficientMaterial;
        if (HalfmoveClock >= 100) return GameStatus.DrawByFiftyMoveRule;
        if (zobristHistory is not null && IsThreefoldRepetition(zobristHistory))
            return GameStatus.DrawByThreefoldRepetition;

        return GameStatus.InProgress;
    }

    /// <summary>True when the position has occurred three times in <paramref name="zobristHistory"/>.</summary>
    public bool IsThreefoldRepetition(IReadOnlyList<ulong> zobristHistory)
    {
        if (zobristHistory.Count == 0) return false;

        var seen = 0;
        foreach (var key in zobristHistory)
            if (key == ZobristKey && ++seen >= 3) return true;

        return false;
    }

    /// <summary>K v K, K+minor v K, and K+B v K+B with both bishops on the same colour.</summary>
    public bool HasInsufficientMaterial()
    {
        var bishops = new List<(Side Side, bool LightSquare)>();
        var minorCount = 0;

        for (var i = 0; i < Square.Count; i++)
        {
            if (_board[i] is not Piece piece) continue;
            switch (piece.Type)
            {
                case PieceType.Pawn:
                case PieceType.Rook:
                case PieceType.Queen:
                    return false;
                case PieceType.Knight:
                    minorCount++;
                    break;
                case PieceType.Bishop:
                    minorCount++;
                    bishops.Add((piece.Side, Square.FromIndex(i).IsLight));
                    break;
            }
        }

        if (minorCount == 0) return true;                       // K v K
        if (minorCount == 1) return true;                       // K+minor v K
        if (minorCount == 2 && bishops.Count == 2)
            return bishops[0].LightSquare == bishops[1].LightSquare; // same-coloured bishops

        return false;
    }

    // --------------------------------------------------------------------------------------
    // Hashing
    // --------------------------------------------------------------------------------------

    private ulong ComputeZobrist()
    {
        var key = 0UL;
        for (var i = 0; i < Square.Count; i++)
            if (_board[i] is Piece piece)
                key ^= Zobrist.PieceSquare[Zobrist.SlotFor(piece), i];

        key ^= Zobrist.Castling[(int)CastlingRights];
        if (EnPassantTarget is Square ep) key ^= Zobrist.EnPassantFile[ep.File];
        if (SideToMove == Side.Black) key ^= Zobrist.SideToMove;
        return key;
    }

    // --------------------------------------------------------------------------------------
    // Rendering (tests and logs only)
    // --------------------------------------------------------------------------------------

    public string ToAsciiBoard()
    {
        var sb = new StringBuilder();
        for (var rank = 7; rank >= 0; rank--)
        {
            sb.Append(rank + 1).Append(' ');
            for (var file = 0; file < 8; file++)
                sb.Append(_board[(rank << 3) | file] is Piece p ? p.ToAsciiGlyph() : '.').Append(' ');
            sb.AppendLine();
        }

        sb.AppendLine("  a b c d e f g h");
        return sb.ToString();
    }

    public override string ToString() => ToFen();

    private readonly struct UndoInfo
    {
        public UndoInfo(
            Move move,
            Piece? captured,
            Square? capturedSquare,
            CastlingRights castling,
            Square? enPassantTarget,
            int halfmoveClock,
            int fullmoveNumber,
            ulong zobrist)
        {
            Move = move;
            Captured = captured;
            CapturedSquare = capturedSquare;
            Castling = castling;
            EnPassantTarget = enPassantTarget;
            HalfmoveClock = halfmoveClock;
            FullmoveNumber = fullmoveNumber;
            Zobrist = zobrist;
        }

        public Move Move { get; }
        public Piece? Captured { get; }
        public Square? CapturedSquare { get; }
        public CastlingRights Castling { get; }
        public Square? EnPassantTarget { get; }
        public int HalfmoveClock { get; }
        public int FullmoveNumber { get; }
        public ulong Zobrist { get; }
    }
}
