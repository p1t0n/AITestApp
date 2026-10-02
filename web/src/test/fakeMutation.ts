// A hook-shaped stand-in for a TanStack mutation result (EXP-82).
//
// The `AgentWidget.*` specs mock the api hooks and render outside a `QueryClientProvider`, so the
// real `useMutation` is not available to them. A plain `{ mutateAsync, isPending }` object was
// enough while every tab mirrored its result into its own `useState`; a tab that reads
// `data`/`error`/`variables` off the mutation needs those fields to actually move, which is what
// this does — around the suite's own `mutateAsync` spy, so its `mockResolvedValue` setup and its
// `toHaveBeenCalledWith` assertions keep working unchanged.
import { useState } from "react";

/** The spy object a spec already keeps for the surface under test. */
interface MutationSpy<Req, Res> {
  mutateAsync: (req: Req) => Promise<Res>;
  isPending: boolean;
}

export function useFakeMutation<Req, Res>(spy: MutationSpy<Req, Res>) {
  const [state, setState] = useState<{ data?: Res; error?: unknown; variables?: Req }>({});
  return {
    data: state.data,
    error: state.error ?? null,
    variables: state.variables,
    isPending: spy.isPending,
    isError: state.error !== undefined,
    isSuccess: state.data !== undefined,
    mutateAsync: spy.mutateAsync,
    // Like the real one: the previous result and error are dropped the moment a run starts.
    mutate: (req: Req) => {
      setState({ variables: req });
      void spy.mutateAsync(req).then(
        (data) => setState({ data, variables: req }),
        (error: unknown) => setState({ error, variables: req }),
      );
    },
  };
}
