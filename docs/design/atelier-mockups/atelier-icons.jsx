/* global React */

// Custom hairline icons — line weight 1.2, calligraphic feel
const Icon = ({ children, size = 16, ...rest }) => (
  <svg
    width={size}
    height={size}
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    strokeWidth="1.4"
    strokeLinecap="round"
    strokeLinejoin="round"
    {...rest}
  >
    {children}
  </svg>
);

const IconPen = (p) => (
  <Icon {...p}>
    <path d="M4 20l1.2-4.2L16 5l3 3L8.2 18.8 4 20z" />
    <path d="M14 7l3 3" />
  </Icon>
);

const IconArchive = (p) => (
  <Icon {...p}>
    <rect x="3" y="5" width="18" height="4" rx="0.5" />
    <path d="M5 9v10h14V9" />
    <path d="M10 13h4" />
  </Icon>
);

const IconAdd = (p) => (
  <Icon {...p}>
    <path d="M12 5v14M5 12h14" />
  </Icon>
);

const IconHome = (p) => (
  <Icon {...p}>
    <path d="M3 11l9-7 9 7" />
    <path d="M5 10v10h14V10" />
  </Icon>
);

const IconChevron = (p) => (
  <Icon {...p}>
    <path d="M9 6l6 6-6 6" />
  </Icon>
);

const IconCopy = (p) => (
  <Icon {...p}>
    <rect x="8" y="8" width="12" height="12" rx="1" />
    <path d="M16 8V5a1 1 0 00-1-1H5a1 1 0 00-1 1v10a1 1 0 001 1h3" />
  </Icon>
);

const IconDownload = (p) => (
  <Icon {...p}>
    <path d="M12 4v12" />
    <path d="M7 11l5 5 5-5" />
    <path d="M4 20h16" />
  </Icon>
);

const IconClose = (p) => (
  <Icon {...p}>
    <path d="M6 6l12 12M18 6L6 18" />
  </Icon>
);

const IconCheck = (p) => (
  <Icon {...p}>
    <path d="M5 12.5l4.5 4.5L19 7" />
  </Icon>
);

const IconUser = (p) => (
  <Icon {...p}>
    <circle cx="12" cy="8" r="3.5" />
    <path d="M5 20c1-3.5 4-5 7-5s6 1.5 7 5" />
  </Icon>
);

const IconLogout = (p) => (
  <Icon {...p}>
    <path d="M9 4H5v16h4" />
    <path d="M15 8l4 4-4 4" />
    <path d="M19 12H9" />
  </Icon>
);

const IconBook = (p) => (
  <Icon {...p}>
    <path d="M4 5a2 2 0 012-2h13v16H6a2 2 0 00-2 2V5z" />
    <path d="M4 19a2 2 0 002 2h13" />
  </Icon>
);

const IconQuill = (p) => (
  <Icon {...p}>
    <path d="M3 21c2-4 6-9 11-13 2-2 5-3 7-3-1 4-2 7-4 9-4 4-9 7-12 7" />
    <path d="M3 21l5-5" />
  </Icon>
);

const IconRefresh = (p) => (
  <Icon {...p}>
    <path d="M4 4v6h6" />
    <path d="M20 20v-6h-6" />
    <path d="M4 10a8 8 0 0114-3" />
    <path d="M20 14a8 8 0 01-14 3" />
  </Icon>
);

const IconPalette = (p) => (
  <Icon {...p}>
    <path d="M12 3a9 9 0 109 9c0-1-1-2-2-2h-2a2 2 0 010-4c1 0 2-1 1-2-2-1-4-1-6-1z" />
    <circle cx="7" cy="11" r="1" />
    <circle cx="10" cy="7" r="1" />
    <circle cx="15" cy="7" r="1" />
  </Icon>
);

const IconLayers = (p) => (
  <Icon {...p}>
    <path d="M12 3l9 5-9 5-9-5 9-5z" />
    <path d="M3 12l9 5 9-5" />
    <path d="M3 16l9 5 9-5" />
  </Icon>
);

Object.assign(window, {
  Icon, IconPen, IconArchive, IconAdd, IconHome, IconChevron,
  IconCopy, IconDownload, IconClose, IconCheck, IconUser,
  IconLogout, IconBook, IconQuill, IconRefresh, IconPalette, IconLayers,
});
