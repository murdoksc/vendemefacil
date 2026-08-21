import { CreditCard } from "lucide-react";
import { useEffect, useState } from "react";
import { PlatformAdminPage } from "./PlatformAdminPage";
import { PlatformSubscriptionsPage } from "./PlatformSubscriptionsPage";

export function PlatformAdminHub({onBack}:{onBack:()=>void}){
  const [section,setSection]=useState<"summary"|"subscriptions">("summary");
  const [authenticated,setAuthenticated]=useState(Boolean(localStorage.getItem("vendemefacil.platform-session")));
  useEffect(()=>{const timer=window.setInterval(()=>setAuthenticated(Boolean(localStorage.getItem("vendemefacil.platform-session"))),500);return()=>window.clearInterval(timer)},[]);
  if(section==="subscriptions")return <PlatformSubscriptionsPage onBack={()=>setSection("summary")}/>;
  return <><PlatformAdminPage onBack={onBack}/>{authenticated&&<button className="admin-subscriptions-shortcut" onClick={()=>setSection("subscriptions")}><CreditCard/>Suscripciones y pagos</button>}</>;
}
