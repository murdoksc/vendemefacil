import { CreditCard } from "lucide-react";
import { useEffect, useState } from "react";
import { PlatformAdminPage } from "./PlatformAdminPage";
import { PlatformSubscriptionsPage } from "./PlatformSubscriptionsPage";
import { PlatformFollowUpsPage } from "./PlatformFollowUpsPage";

export function PlatformAdminHub({onBack}:{onBack:()=>void}){
  const [section,setSection]=useState<"summary"|"subscriptions"|"followups">("summary");
  const [authenticated,setAuthenticated]=useState(Boolean(localStorage.getItem("vendemefacil.platform-session")));
  const [activation,setActivation]=useState<number|null>(null);
  const [pending,setPending]=useState(0);
  useEffect(()=>{const timer=window.setInterval(()=>setAuthenticated(Boolean(localStorage.getItem("vendemefacil.platform-session"))),500);return()=>window.clearInterval(timer)},[]);
  useEffect(()=>{const raw=localStorage.getItem("vendemefacil.platform-session");if(!raw)return;const session=JSON.parse(raw);fetch("/api/platform/dashboard",{headers:{Authorization:`Bearer ${session.accessToken}`}}).then(r=>r.ok?r.json():null).then(data=>{if(!data)return;setActivation(data.tenants.length?Math.round(data.tenants.reduce((sum:number,x:{activationSteps:number})=>sum+x.activationSteps,0)/(data.tenants.length*7)*100):0);setPending(data.followUps.trialsExpiring.length+data.followUps.withoutFirstSale.length+data.followUps.planChangeRequests.length)}).catch(()=>undefined)},[authenticated]);
  if(section==="subscriptions")return <PlatformSubscriptionsPage onBack={()=>setSection("summary")}/>;
  if(section==="followups")return <PlatformFollowUpsPage onBack={()=>setSection("summary")}/>;
  return <><PlatformAdminPage onBack={onBack}/>{authenticated&&<div className="admin-hub-shortcuts"><button className="admin-subscriptions-shortcut" onClick={()=>setSection("subscriptions")}><CreditCard/><span>Suscripciones y pagos{activation!==null&&<small>Activación promedio: {activation}%</small>}</span></button><button className="admin-subscriptions-shortcut" onClick={()=>setSection("followups")}><span>Seguimientos pendientes<small>{pending} acciones por revisar</small></span></button></div>}</>;
}
