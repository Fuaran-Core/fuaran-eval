/// The provider seam: what an eval harness needs from a model provider, and
/// nothing else. A domain's own client abstraction — which will carry its own
/// closed provider vocabulary — binds this seam through `ofFuncs` and is
/// therefore its FIRST PROVIDER rather than a competing definition.
///
/// The record shapes live here rather than beside a binding because they are
/// the wire between the harness and any provider: a second domain's harness
/// needs `EvalCompletion`'s six usage counters (the cost model is the same
/// arithmetic whatever the artefact class) without inheriting another domain's
/// provider type.
module Fuaran.Eval.Core.EvalProvider

/// One message exchanged with a provider.
type EvalMessage = { Role: string; Content: string }

/// A provider's response to a single invocation.
type EvalCompletion =
    {
        OutputText: string
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

/// The generic provider seam. `ProviderKey` is a string rather than a domain
/// DU deliberately: it is what lands in a result cell's `provider` field, and
/// the census's cross-family threshold reads its prefix — neither needs the
/// closed set, and requiring one is what would stop a second domain adopting
/// this file unchanged.
type IEvalProvider =
    abstract ProviderKey: string
    abstract ModelId: string
    abstract Invoke: systemPrompt: string * messages: EvalMessage list -> Async<EvalCompletion>

/// Build a provider from functions — the adapter shape a domain binding uses
/// to present its own client through this seam without inheriting from it.
let ofFuncs
    (providerKey: string)
    (modelId: string)
    (invoke: string -> EvalMessage list -> Async<EvalCompletion>)
    : IEvalProvider =
    { new IEvalProvider with
        member _.ProviderKey = providerKey
        member _.ModelId = modelId
        member _.Invoke(systemPrompt, messages) = invoke systemPrompt messages }

/// The FAKE-REPLAY posture, typed. A replay provider serves pre-recorded
/// completions keyed by the cell id a caller asks for, and **fails on a miss
/// rather than generating** — that refusal is the whole posture. A fake that
/// silently invents a completion turns a replay run into an unlabelled live
/// run, which is indistinguishable from the real thing in the result file and
/// is the one failure a replay harness must not be able to have.
let replay (providerKey: string) (modelId: string) (recorded: Map<string, EvalCompletion>) : IEvalProvider =
    ofFuncs providerKey modelId (fun _systemPrompt messages ->
        async {
            let key =
                messages
                |> List.tryLast
                |> Option.map (fun m -> m.Content)
                |> Option.defaultValue ""

            match Map.tryFind key recorded with
            | Some completion -> return completion
            | None ->
                return
                    failwith
                        $"replay provider '{providerKey}' has no recorded completion for this invocation \
                          ({recorded.Count} recorded). A replay run must not generate on a miss."
        })

/// A provider that is declared but not wired — surfaces a friendly error rather
/// than producing a fake completion. Same refusal as `replay`, for the case
/// where nothing was recorded at all.
let notWired (providerKey: string) : IEvalProvider =
    ofFuncs providerKey $"{providerKey}-not-yet-wired" (fun _ _ ->
        async { return failwith $"Provider {providerKey} client is not yet implemented." })
