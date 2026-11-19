import {Easing} from "remotion";
// 取自遊戲內揭曉牌：墨綠底、黃銅框、襯線字
export const theme = {
  colors: {felt: "#0B3B2A", feltDeep: "#06241A", brass: "#D1AC5E", cream: "#F6EEDC", ink: "rgba(6,20,14,0.82)", white: "#FFFFFF"},
  font: {sans: "'Helvetica Neue', Helvetica, Arial, sans-serif"},
  ease: {out: Easing.bezier(0.16, 1, 0.3, 1), in: Easing.bezier(0.7, 0, 0.84, 0), inOut: Easing.bezier(0.65, 0, 0.35, 1)},
  spring: {smooth: {damping: 200, stiffness: 110}, snappy: {damping: 22, stiffness: 190}},
};
