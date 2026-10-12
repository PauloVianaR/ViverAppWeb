export { request, download, uploadTo, showDialog, closeDialog, focus } from "./patient-experience.js";
export function openServicePicker(triggerId, pickerId) {
    const trigger=document.getElementById(triggerId), picker=document.getElementById(pickerId);
    if(!trigger || !picker)return;
    if(picker.matches(":popover-open")){picker.hidePopover();return;}
    const rect=trigger.getBoundingClientRect();
    const width=Math.min(Math.max(rect.width,360),window.innerWidth-24);
    picker.style.width=width+"px";
    picker.style.left=Math.max(12,Math.min(rect.left,window.innerWidth-width-12))+"px";
    picker.style.top=Math.min(rect.bottom+6,Math.max(12,window.innerHeight-330))+"px";
    picker.showPopover();
    picker.querySelector("input")?.focus();
}
