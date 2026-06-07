import type { KerbcamClient } from "@jonpepler/kerbcam";
import {
  createContext,
  useContext,
  useMemo,
  useRef,
  type ReactNode,
} from "react";

// ---------------------------------------------------------------------------
// Subscriptions seam
// ---------------------------------------------------------------------------

/**
 * Lifecycle controller for per-camera slot subscriptions. The default
 * implementation refcounts so multiple widgets sharing the same flightId
 * share one slot.
 */
export interface KerbcamSubscriptions {
  acquire(flightId: number): void;
  release(flightId: number): void;
}

/** Build the default refcounted subscriptions implementation for a client. */
export function createClientSubscriptions(
  client: KerbcamClient,
): KerbcamSubscriptions {
  const refcounts = new Map<number, number>();
  return {
    acquire(flightId: number): void {
      const count = (refcounts.get(flightId) ?? 0) + 1;
      refcounts.set(flightId, count);
      if (count === 1) {
        client.subscribe(flightId).catch(() => {});
      }
    },
    release(flightId: number): void {
      const count = (refcounts.get(flightId) ?? 1) - 1;
      if (count <= 0) {
        refcounts.delete(flightId);
        client.unsubscribe(flightId).catch(() => {});
      } else {
        refcounts.set(flightId, count);
      }
    },
  };
}

// ---------------------------------------------------------------------------
// Context shape
// ---------------------------------------------------------------------------

interface KerbcamContextValue {
  client: KerbcamClient;
  subscriptions: KerbcamSubscriptions;
}

const KerbcamContext = createContext<KerbcamContextValue | null>(null);

// ---------------------------------------------------------------------------
// Provider
// ---------------------------------------------------------------------------

export interface KerbcamProviderProps {
  client: KerbcamClient;
  /** Override the subscription manager. Defaults to a refcounted per-client impl. */
  subscriptions?: KerbcamSubscriptions;
  children: ReactNode;
}

/**
 * Provide a `KerbcamClient` (and optional subscriptions override) to the
 * component subtree. Hooks and `CameraFeed` read both from this context.
 */
export function KerbcamProvider({
  client,
  subscriptions: subscriptionsProp,
  children,
}: KerbcamProviderProps): React.JSX.Element {
  // Create default subscriptions once per client. A ref so the object is
  // stable across renders even if the parent re-renders.
  const defaultSubsRef = useRef<KerbcamSubscriptions | null>(null);
  if (defaultSubsRef.current === null) {
    defaultSubsRef.current = createClientSubscriptions(client);
  }

  const value = useMemo(
    () => ({
      client,
      subscriptions: subscriptionsProp ?? defaultSubsRef.current!,
    }),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [client, subscriptionsProp],
  );

  return (
    <KerbcamContext.Provider value={value}>{children}</KerbcamContext.Provider>
  );
}

// ---------------------------------------------------------------------------
// Hooks
// ---------------------------------------------------------------------------

/**
 * Return the `KerbcamClient` from the nearest `KerbcamProvider`.
 * Throws if called outside a provider.
 */
export function useKerbcamClient(): KerbcamClient {
  const ctx = useContext(KerbcamContext);
  if (!ctx) {
    throw new Error("useKerbcamClient must be used inside a KerbcamProvider");
  }
  return ctx.client;
}

/**
 * Return the `KerbcamSubscriptions` from the nearest `KerbcamProvider`.
 * Throws if called outside a provider.
 */
export function useKerbcamSubscriptions(): KerbcamSubscriptions {
  const ctx = useContext(KerbcamContext);
  if (!ctx) {
    throw new Error(
      "useKerbcamSubscriptions must be used inside a KerbcamProvider",
    );
  }
  return ctx.subscriptions;
}
