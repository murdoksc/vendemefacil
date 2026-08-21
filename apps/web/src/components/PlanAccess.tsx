import {
  createContext,
  ReactNode,
  useContext,
  useEffect,
  useState,
} from "react";
import { ArrowRight, LockKeyhole, X } from "lucide-react";
import { apiRequest, AuthSession } from "../lib/api";
type Capabilities = {
  code: string;
  name: string;
  monthlyPrice: number;
  maxUsers: number;
  maxBranches: number;
  emailAndWhatsApp: boolean;
  silentPrinting: boolean;
  fullReports: boolean;
  customBranding: boolean;
  securityAudit: boolean;
};
type Subscription = {
  planCode: string;
  subscriptionStatus: string;
  trialEndsAtUtc: string;
  currentPeriodEndsAtUtc: string | null;
  graceEndsAtUtc: string | null;
  capabilities: Capabilities;
};
type Access = {
  subscription: Subscription | null;
  require: (
    feature: keyof Pick<
      Capabilities,
      "emailAndWhatsApp" | "silentPrinting" | "fullReports" | "customBranding" | "securityAudit"
    >,
    label: string,
  ) => boolean;
};
const Context = createContext<Access>({
  subscription: null,
  require: () => false,
});
export const usePlanAccess = () => useContext(Context);
export const showUpgradeRequired = (label: string) =>
  window.dispatchEvent(
    new CustomEvent("vendemefacil:upgrade-required", { detail: label }),
  );
export function PlanAccessProvider({
  session,
  children,
}: {
  session: AuthSession;
  children: ReactNode;
}) {
  const [subscription, setSubscription] = useState<Subscription | null>(null),
    [locked, setLocked] = useState(""),
    [message, setMessage] = useState("");
  useEffect(() => {
    apiRequest<Subscription>("/api/v1/subscription", {}, session)
      .then(setSubscription)
      .catch(() => undefined);
  }, [session]);
  useEffect(() => {
    const listener = (event: Event) =>
      setLocked((event as CustomEvent<string>).detail);
    window.addEventListener("vendemefacil:upgrade-required", listener);
    return () =>
      window.removeEventListener("vendemefacil:upgrade-required", listener);
  }, []);
  function require(
    feature: keyof Pick<
      Capabilities,
      "emailAndWhatsApp" | "silentPrinting" | "fullReports" | "customBranding" | "securityAudit"
    >,
    label: string,
  ) {
    if (subscription?.capabilities[feature]) return true;
    setLocked(label);
    setMessage("");
    return false;
  }
  async function requestUpgrade() {
    try {
      const result = await apiRequest<{ message: string }>(
        "/api/v1/subscription/change-request",
        { method: "POST", body: JSON.stringify({ planCode: "negocio" }) },
        session,
      );
      setMessage(result.message);
    } catch (e) {
      setMessage(
        e instanceof Error ? e.message : "No pudimos enviar la solicitud.",
      );
    }
  }
  const end =
      subscription?.subscriptionStatus === "Trial"
        ? subscription.trialEndsAtUtc
        : subscription?.currentPeriodEndsAtUtc,
    days = end
      ? Math.ceil((new Date(end).getTime() - Date.now()) / 86400000)
      : null;
  const notice =
    days === null
      ? ""
      : days <= 0
        ? "Tu prueba terminó. Solicita la activación para conservar el acceso."
        : days === 1
          ? "Tu prueba termina mañana."
          : days <= 3
            ? `Tu prueba termina en ${days} días. Es momento de elegir tu plan.`
            : days <= 7
              ? `Te quedan ${days} días de prueba.`
              : `Prueba del plan ${subscription?.capabilities.name} · ${days} días restantes`;
  return (
    <Context.Provider value={{ subscription, require }}>
      {subscription?.subscriptionStatus === "Trial" && days !== null && (
        <div className={`trial-banner ${days <= 3 ? "urgent" : ""}`}>
          <strong>{notice}</strong>
          {days <= 7 && (
            <button
              onClick={() =>
                showUpgradeRequired("Continúa usando VéndemeFácil")
              }
            >
              Ver plan Negocio
            </button>
          )}
        </div>
      )}
      {children}
      {locked && (
        <div className="modal-backdrop">
          <section className="upgrade-modal">
            <button className="close-form" onClick={() => setLocked("")}>
              <X />
            </button>
            <span>
              <LockKeyhole />
            </span>
            <p className="eyebrow">FUNCIÓN DE PLAN NEGOCIO</p>
            <h2>{locked}</h2>
            <p>
              Esta función está disponible desde el plan Negocio de $499 MXN al
              mes. Tu prueba actual corresponde al plan Esencial.
            </p>
            {message ? (
              <div className="form-success">{message}</div>
            ) : (
              <button
                className="button primary"
                onClick={() => void requestUpgrade()}
              >
                Solicitar cambio de plan <ArrowRight />
              </button>
            )}
            <button className="upgrade-later" onClick={() => setLocked("")}>
              Continuar con Esencial
            </button>
          </section>
        </div>
      )}
    </Context.Provider>
  );
  /* Legacy banner retained below only to keep this patch independent from its original file encoding.
 return <Context.Provider value={{subscription,require}}>{subscription?.subscriptionStatus==="Trial"&&days!==null&&<div className="trial-banner">Prueba del plan <strong>{subscription.capabilities.name}</strong> · {Math.max(0,days)} días restantes</div>}{children}{locked&&<div className="modal-backdrop"><section className="upgrade-modal"><button className="close-form" onClick={()=>setLocked("")}><X/></button><span><LockKeyhole/></span><p className="eyebrow">FUNCIÓN DE PLAN NEGOCIO</p><h2>{locked}</h2><p>Esta función está disponible desde el plan Negocio de $499 MXN al mes. Tu prueba actual corresponde al plan Esencial.</p>{message?<div className="form-success">{message}</div>:<button className="button primary" onClick={()=>void requestUpgrade()}>Solicitar cambio de plan <ArrowRight/></button>}<button className="upgrade-later" onClick={()=>setLocked("")}>Continuar con Esencial</button></section></div>}</Context.Provider>;
 */
}
