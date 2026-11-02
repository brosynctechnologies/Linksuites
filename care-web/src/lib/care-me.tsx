"use client";

import { createContext, useContext, type ReactNode } from "react";
import { careAccess, type CareAccess } from "./roles";
import type { CareMeDto } from "./types";

export interface CareSession {
  me: CareMeDto;
  access: CareAccess;
}

const CareMeContext = createContext<CareSession | null>(null);

/** Provided by the staff app shell once `GET /api/care/staff/me` has answered; pages never render before it. */
export function CareMeProvider({ me, support, children }: { me: CareMeDto; support: boolean; children: ReactNode }) {
  return <CareMeContext.Provider value={{ me, access: careAccess(me, support) }}>{children}</CareMeContext.Provider>;
}

/** The signed-in member and what they may do. Only inside the staff app (`/app`). */
export function useCare(): CareSession {
  const value = useContext(CareMeContext);
  if (!value) throw new Error("useCare is used outside the staff app.");
  return value;
}
