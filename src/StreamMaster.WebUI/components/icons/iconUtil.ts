import { baseHostURL, isDev } from "@lib/settings";

export const SMLogo = "/images/streammaster_logo.png";

export function getIconUrl(source?: string): string {
  const raw = source && source !== "" ? source : SMLogo;

  // absolute URLs just pass through
  if (raw.startsWith("http")) {
    console.log("absolute URL for SMLogo: ", raw);
    return raw;
  }

  return `${baseHostURL}${raw}`;
}