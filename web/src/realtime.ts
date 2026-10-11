import { useEffect, useState } from "react";
import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { api, csrf } from "./api";
export function useRealtime(connected: boolean, onChange: () => void) {
  const [state, setState] = useState("未连接");
  useEffect(() => {
    if (!connected) return;
    let after = 0,
      stopped = false,
      busy = false;
    const connection = new HubConnectionBuilder()
      .withUrl("/hubs/events", { headers: { "X-CSRF-TOKEN": csrf } })
      .withAutomaticReconnect([0, 2000, 5000, 15000])
      .configureLogging(LogLevel.None)
      .build();
    const catchup = async () => {
      if (busy || stopped) return;
      busy = true;
      try {
        let rows;
        do {
          rows = await api<{ id: number }[]>("/changes?after=" + after);
          if (rows.length) {
            after = rows.at(-1)!.id;
            onChange();
          }
        } while (rows.length === 200 && !stopped);
      } catch {
        setState("同步中断");
      } finally {
        busy = false;
      }
    };
    connection.on("changes", () => {
      void catchup();
    });
    connection.onreconnecting(() => setState("正在重连"));
    connection.onreconnected(() => {
      setState("实时连接");
      void catchup();
    });
    connection.onclose(() => setState("连接中断"));
    const start = async () => {
      try {
        await connection.start();
        if (stopped) {
          await connection.stop();
          return;
        }
        setState("实时连接");
        await catchup();
      } catch {
        setState("连接中断");
      }
    };
    void start();
    const timer = setInterval(() => {
      if (connection.state === "Disconnected") void start();
      else void catchup();
    }, 10000);
    return () => {
      stopped = true;
      clearInterval(timer);
      void connection.stop();
    };
  }, [connected, onChange]);
  return state;
}
