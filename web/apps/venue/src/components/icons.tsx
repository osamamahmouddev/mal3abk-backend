import type { SVGProps } from "react";

/** Icons Lucide lacks, drawn on the same 24px grid and stroke so they sit next to Lucide icons. */
type IconProps = SVGProps<SVGSVGElement> & { size?: number | string; strokeWidth?: number | string };

function base({ size = 24, strokeWidth = 2, ...rest }: IconProps): SVGProps<SVGSVGElement> {
  return {
    xmlns: "http://www.w3.org/2000/svg",
    width: size,
    height: size,
    viewBox: "0 0 24 24",
    fill: "none",
    stroke: "currentColor",
    strokeWidth,
    strokeLinecap: "round",
    strokeLinejoin: "round",
    "aria-hidden": true,
    ...rest,
  };
}

export function SoccerBall(props: IconProps) {
  return (
    <svg {...base(props)}>
      <circle cx="12" cy="12" r="10" />
      <path d="m12 7 4.2 3.1-1.6 5h-5.2l-1.6-5Z" />
      <path d="M12 7V2.5M16.2 10.1l4.4-1.6M14.6 15.1l2.8 3.8M9.4 15.1l-2.8 3.8M7.8 10.1 3.4 8.5" />
    </svg>
  );
}

export function Pitch(props: IconProps) {
  return (
    <svg {...base(props)}>
      <rect x="2" y="4" width="20" height="16" rx="2" />
      <path d="M12 4v16" />
      <circle cx="12" cy="12" r="3" />
      <path d="M2 9h3v6H2M22 9h-3v6h3" />
    </svg>
  );
}
