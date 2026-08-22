/// The provider seam: what an eval harness needs from a model provider, and
/// nothing else. A domain's own client abstraction — which will carry its own
/// closed provider vocabulary — binds this seam through `ofFuncs` and is
/// therefore its FIRST PROVIDER rather than a competing definition.
///
/// The record shapes live here rather than beside a binding because they are
/// the wire between the harness and any provider: a second domain's harness
/// needs the usage counters (the cost model is the same arithmetic whatever the
/// artefact class) without inheriting another domain's provider type.
///
/// **What a provider RETURNS is a type parameter, and that is the whole design.**
/// The first binding this seam was drawn against answered in text, so the seam
/// said `OutputText: string` and every other shape had to flatten into it. The
/// second binding answers in BRANCHES — its provider reduces a response to one
/// of a small closed set of outcomes, and the branch *is* the result its scorer
/// reads. Flattening that to text discards the discrimination; carrying it as
/// `'Result` discards nothing, and costs the substrate no vocabulary at all,
/// because a type parameter is exactly the thing that can be instantiated
/// without being named. A text-shaped harness instantiates it at `string` and
/// is where it started; a branch-shaped harness instantiates it at its own
/// closed union and keeps every case.
///
/// Note what this deliberately does NOT do: it does not model failure. A
/// "failed" case belongs to a domain's result union if that domain wants one,
/// because deciding that an emission is bad is a judgement about emitted
/// content — the judgement this substrate does not make anywhere else either.
module Fuaran.Eval.Core.EvalProvider

/// One message exchanged with a provider.
type EvalMessage = { Role: string; Content: string }

/// One invocation, as the harness asks for it.
type EvalRequest =
    {
        /// The key this invocation is known by — **supplied by the caller**, and
        /// deliberately not derived here. A replay provider looks a recording up
        /// by it, so it decides what "the same invocation" means, and that is a
        /// question only the harness can answer: a conversational harness keys on
        /// the turn being asked, a case-driven one keys on the case id, and a
        /// substrate that picked either would silently be wrong for the other.
        /// `requestKeyedOnLastMessage` is the conversational default, written
        /// once at the call site rather than hidden inside the replay.
        InvocationKey: string
        SystemPrompt: string
        Messages: EvalMessage list
    }

/// What one invocation cost. Separated from the completion because it is the
/// arithmetic every cost model needs and none of it depends on what the
/// provider actually returned.
type EvalUsage =
    {
        /// Tokens processed at full price (no cache hit).
        InputTokens: int
        /// BILLED output tokens — includes any hidden reasoning/thinking tokens
        /// (the number the provider charges the output rate on).
        OutputTokens: int
        /// The hidden reasoning/thinking share of `OutputTokens` (0 when the
        /// model emitted none, or the provider doesn't report a split). The
        /// TEXT emission size — the compactness measurand — is
        /// `OutputTokens - ReasoningOutputTokens`; cost uses `OutputTokens`.
        ReasoningOutputTokens: int
        /// Tokens written to the prompt cache this turn (paid above input rate).
        CacheCreationInputTokens: int
        /// Tokens served from the prompt cache this turn (paid below input rate).
        CacheReadInputTokens: int
        WallClockMs: int
    }

/// A provider's response to a single invocation, over whatever that provider
/// resolves a response to.
type EvalCompletion<'Result> =
    {
        /// The provider's answer, in the harness's own shape. Text, a closed
        /// union of emission branches, a parsed structure — the substrate carries
        /// it and reads none of it.
        Result: 'Result
        /// What the invocation cost, when the provider reports it. **`None` means
        /// unreported, never zero.** A provider that returns no usage payload —
        /// and a real one does exist, where the response is a tool branch and the
        /// harness never asked the transport for counters — would otherwise be
        /// indistinguishable from one that ran free, and a cohort's cost figure
        /// would read as measured when it was invented. The same instinct as
        /// every empty field on a provenance stamp: empty means unstamped.
        Usage: EvalUsage option
    }

/// The generic provider seam. `ProviderKey` is a string rather than a domain
/// DU deliberately: it is what lands in a result cell's `provider` field, and
/// the census's cross-family threshold reads its prefix — neither needs the
/// closed set, and requiring one is what would stop a second domain adopting
/// this file unchanged.
type IEvalProvider<'Result> =
    abstract ProviderKey: string
    abstract ModelId: string
    abstract Invoke: request: EvalRequest -> Async<EvalCompletion<'Result>>

/// The text-shaped instantiation, named because a harness whose provider answers
/// in prose is the common case and should not have to spell the parameter out.
/// It is an abbreviation, not a second seam.
type ITextEvalProvider = IEvalProvider<string>

/// A request whose key is chosen explicitly — the shape a case-driven harness
/// has, where the case id already identifies the invocation.
let request (invocationKey: string) (systemPrompt: string) (messages: EvalMessage list) : EvalRequest =
    { InvocationKey = invocationKey
      SystemPrompt = systemPrompt
      Messages = messages }

/// A request keyed on the LAST message's content — the shape a conversational
/// harness has, where the turn being asked is the identity of the invocation.
let requestKeyedOnLastMessage (systemPrompt: string) (messages: EvalMessage list) : EvalRequest =
    let key = messages |> List.tryLast |> Option.map _.Content |> Option.defaultValue ""

    request key systemPrompt messages

/// Build a provider from functions — the adapter shape a domain binding uses
/// to present its own client through this seam without inheriting from it.
let ofFuncs
    (providerKey: string)
    (modelId: string)
    (invoke: EvalRequest -> Async<EvalCompletion<'Result>>)
    : IEvalProvider<'Result> =
    { new IEvalProvider<'Result> with
        member _.ProviderKey = providerKey
        member _.ModelId = modelId
        member _.Invoke(request) = invoke request }

/// The FAKE-REPLAY posture, typed. A replay provider serves pre-recorded
/// completions keyed by the invocation key the CALLER put on the request, and
/// **fails on a miss rather than generating** — that refusal is the whole
/// posture. A fake that silently invents a completion turns a replay run into an
/// unlabelled live run, which is indistinguishable from the real thing in the
/// result file and is the one failure a replay harness must not be able to have.
///
/// The refusal survives the result becoming a type parameter, and could not have
/// failed to: refusing means raising, and raising needs no value of the type it
/// declines to produce. A seam that had modelled failure as a case of the result
/// would have handed every replay a way to answer a miss politely.
let replay
    (providerKey: string)
    (modelId: string)
    (recorded: Map<string, EvalCompletion<'Result>>)
    : IEvalProvider<'Result> =
    ofFuncs providerKey modelId (fun request ->
        async {
            match Map.tryFind request.InvocationKey recorded with
            | Some completion -> return completion
            | None ->
                return
                    failwith
                        $"replay provider '{providerKey}' has no recorded completion for invocation key \
                          '{request.InvocationKey}' ({recorded.Count} recorded). A replay run must not \
                          generate on a miss."
        })

/// A provider that is declared but not wired — surfaces a friendly error rather
/// than producing a fake completion. Same refusal as `replay`, for the case
/// where nothing was recorded at all.
let notWired (providerKey: string) : IEvalProvider<'Result> =
    ofFuncs providerKey $"{providerKey}-not-yet-wired" (fun _ ->
        async { return failwith $"Provider {providerKey} client is not yet implemented." })
