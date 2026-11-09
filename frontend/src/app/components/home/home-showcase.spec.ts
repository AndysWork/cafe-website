import { HomeComponent, selectLandingSections, selectShowcaseItem } from './home.component';
import { ElementRef } from '@angular/core';
import { fakeAsync, tick } from '@angular/core/testing';
import { of, throwError } from 'rxjs';

describe('landing menu showcase', () => {
  const item = (name: string, extra: Record<string, unknown> = {}) => ({
    id: name, categoryId: 'category', subCategoryId: '', catalogueName: name, name, ...extra
  });

  it('uses an available, visible product image and its actual web price', () => {
    const result = selectShowcaseItem([
      item('Veg Burger', { webPrice: 40 }),
      item('Hidden Burger', { imageUrl: 'hidden.jpg', isVisibleToCustomers: false }),
      item('Sold Out Burger', { imageUrl: 'sold.jpg', isAvailable: false }),
      item('Burger Add-on', { imageUrl: 'addon.jpg', isAddOnOnly: true }),
      item('Chicken Burger', { imageUrl: 'burger.jpg', webPrice: 90, onlinePrice: 100 })
    ], 'burger', '🍔');
    expect(result).toEqual({ name: 'Chicken Burger', imageUrl: 'burger.jpg', price: 90, icon: '🍔' });
  });

  it('uses a fallback icon and no invented price, or omits absent categories', () => {
    expect(selectShowcaseItem([item('Veg Momo')], 'momo', '🥟'))
      .toEqual({ name: 'Veg Momo', imageUrl: '', price: null, icon: '🥟' });
    expect(selectShowcaseItem([item('Veg Momo')], 'pasta', '🍝')).toBeNull();
  });

  it('keeps configured discovery items in order and selects different visible specials', () => {
    const burger = item('Veg Burger', { imageUrl: 'burger.jpg', webPrice: 75 });
    const pasta = item('Veg Pasta', { imageUrl: 'pasta.jpg' });
    const hidden = item('Hidden Pizza', { imageUrl: 'hidden.jpg', isVisibleToCustomers: false });
    const specialBurger = item('Chicken Burger', { imageUrl: 'chicken.jpg', webPrice: 120 });
    const sections = selectLandingSections([burger, pasta, hidden, specialBurger], [pasta, burger, hidden], true);
    expect(sections.discovery.map(entry => entry.name)).toEqual(['Veg Pasta', 'Veg Burger']);
    expect(sections.specials).toEqual([{ name: 'Chicken Burger', imageUrl: 'chicken.jpg', price: 120, icon: '🍔' }]);
  });

  it('uses distinct image-bearing discovery items when no featured items are configured', () => {
    const sections = selectLandingSections([
      item('Veg Burger', { imageUrl: 'burger.jpg' }),
      item('Fruit Salad', { imageUrl: 'salad.jpg' }),
      item('Fruit Salad', { imageUrl: 'duplicate.jpg' }),
      item('Sold Out Soup', { imageUrl: 'soup.jpg', isAvailable: false }),
      item('Add-on Salad', { imageUrl: 'addon.jpg', isAddOnOnly: true }),
      item('Tea')
    ], [], false);
    expect(sections.specials.map(entry => entry.name)).toEqual(['Veg Burger']);
    expect(sections.discovery.map(entry => entry.name)).toEqual(['Fruit Salad']);
    expect(selectLandingSections([item('Fruit Salad', { imageUrl: 'salad.jpg' })], [], true).discovery).toEqual([]);
  });

  it('prefers different menu categories for the discovery gallery', () => {
    const sections = selectLandingSections([
      item('Fruit Salad', { imageUrl: 'salad.jpg', categoryId: 'salads' }),
      item('Garden Salad', { imageUrl: 'garden.jpg', categoryId: 'salads' }),
      item('Tomato Soup', { imageUrl: 'soup.jpg', categoryId: 'soups' })
    ], [], false);
    expect(sections.discovery.map(entry => entry.name)).toEqual(['Fruit Salad', 'Tomato Soup', 'Garden Salad']);
  });

  it('advances every three seconds, wraps at the end, and stops on destruction', fakeAsync(() => {
    const carousel = document.createElement('div');
    const card = document.createElement('button');
    card.className = 'special-tile';
    Object.defineProperty(card, 'offsetWidth', { value: 180 });
    Object.defineProperty(carousel, 'scrollWidth', { value: 600 });
    Object.defineProperty(carousel, 'clientWidth', { value: 200 });
    Object.defineProperty(carousel, 'scrollLeft', { value: 0, writable: true });
    carousel.appendChild(card);
    const scroll = jasmine.createSpy('scrollBy');
    const reset = jasmine.createSpy('scrollTo');
    carousel.scrollBy = scroll;
    carousel.scrollTo = reset;
    spyOn(carousel, 'matches').and.returnValue(false);
    spyOnProperty(document, 'hidden', 'get').and.returnValue(false);
    const media = jasmine.createSpyObj<MediaQueryList>('media', ['addEventListener'], { matches: false });
    spyOn(window, 'matchMedia').and.returnValue(media);
    const component: HomeComponent = Object.create(HomeComponent.prototype);
    component.specialsCarousel = new ElementRef(carousel);
    component.startSpecialsCarousel();
    tick(2999);
    expect(scroll).not.toHaveBeenCalled();
    tick(1);
    expect(scroll).toHaveBeenCalledWith({ left: 192, behavior: 'auto' });
    carousel.scrollLeft = 400;
    tick(3000);
    expect(reset).toHaveBeenCalledWith({ left: 0, behavior: 'instant' });
    component.ngOnDestroy();
    tick(3000);
    expect(reset).toHaveBeenCalledTimes(1);
  }));

  it('advances categories every three seconds, repeats the loop and clears its timer', fakeAsync(() => {
    const carousel = document.createElement('nav');
    const card = document.createElement('button');
    Object.defineProperty(card, 'offsetWidth', { value: 76 });
    Object.defineProperty(carousel, 'scrollWidth', { value: 600 });
    Object.defineProperty(carousel, 'clientWidth', { value: 200 });
    Object.defineProperty(carousel, 'scrollLeft', { value: 0, writable: true });
    carousel.appendChild(card);
    const scroll = jasmine.createSpy('scrollBy');
    const reset = jasmine.createSpy('scrollTo');
    carousel.scrollBy = scroll;
    carousel.scrollTo = reset;
    spyOn(carousel, 'matches').and.returnValue(false);
    spyOnProperty(document, 'hidden', 'get').and.returnValue(false);
    spyOn(window, 'matchMedia').and.returnValue(
      jasmine.createSpyObj<MediaQueryList>('media', ['addEventListener'], { matches: false })
    );
    const component: HomeComponent = Object.create(HomeComponent.prototype);
    component.categoryCarousel = new ElementRef(carousel);
    component.startSpecialsCarousel();
    tick(2999);
    expect(scroll).not.toHaveBeenCalled();
    tick(1);
    expect(scroll).toHaveBeenCalledWith({ left: 76, behavior: 'auto' });
    carousel.scrollLeft = 400;
    tick(3000);
    expect(reset).toHaveBeenCalledWith({ left: 0, behavior: 'instant' });
    carousel.scrollLeft = 0;
    tick(3000);
    expect(scroll).toHaveBeenCalledTimes(2);
    component.ngOnDestroy();
    tick(3000);
    expect(scroll).toHaveBeenCalledTimes(2);
  }));

  it('continues forward across the repeated mobile category set', () => {
    const carousel = document.createElement('nav');
    carousel.innerHTML = '<button></button><div class="category-repeat"><button></button></div>';
    const first = carousel.querySelector('button')!;
    const repeat = carousel.querySelector('.category-repeat button')!;
    Object.defineProperty(first, 'offsetWidth', { value: 76 });
    Object.defineProperty(first, 'offsetLeft', { value: 0 });
    Object.defineProperty(repeat, 'offsetLeft', { value: 988 });
    Object.defineProperty(carousel, 'scrollWidth', { value: 1976 });
    Object.defineProperty(carousel, 'clientWidth', { value: 300 });
    Object.defineProperty(carousel, 'scrollLeft', { value: 1064, writable: true });
    const scroll = jasmine.createSpy('scrollBy');
    const reset = jasmine.createSpy('scrollTo');
    carousel.scrollBy = scroll;
    carousel.scrollTo = reset;
    spyOn(carousel, 'matches').and.returnValue(false);
    spyOnProperty(document, 'hidden', 'get').and.returnValue(false);
    spyOn(window, 'matchMedia').and.callFake(query =>
      jasmine.createSpyObj<MediaQueryList>('media', ['addEventListener'], { matches: query === '(max-width: 600px)' })
    );
    const component: HomeComponent = Object.create(HomeComponent.prototype);
    component.categoryCarousel = new ElementRef(carousel);
    component.advanceCategories();
    expect(reset).toHaveBeenCalledWith({ left: 76, behavior: 'instant' });
    expect(scroll).toHaveBeenCalledWith({ left: 76, behavior: 'auto' });
  });

  it('advances artwork at five seconds, loops and stops on destruction', fakeAsync(() => {
    const carousel = document.createElement('div');
    const slide = document.createElement('div');
    slide.className = 'visit-art-slide';
    carousel.appendChild(slide);
    Object.defineProperty(slide, 'offsetWidth', { value: 300 });
    Object.defineProperty(carousel, 'scrollWidth', { value: 900 });
    Object.defineProperty(carousel, 'clientWidth', { value: 300 });
    Object.defineProperty(carousel, 'scrollLeft', { value: 0, writable: true });
    const scroll = jasmine.createSpy('scrollBy');
    const reset = jasmine.createSpy('scrollTo');
    carousel.scrollBy = scroll;
    carousel.scrollTo = reset;
    spyOn(carousel, 'matches').and.returnValue(false);
    spyOnProperty(document, 'hidden', 'get').and.returnValue(false);
    spyOn(window, 'matchMedia').and.returnValue(
      jasmine.createSpyObj<MediaQueryList>('media', ['addEventListener'], { matches: false })
    );
    const component: HomeComponent = Object.create(HomeComponent.prototype);
    component.visitCarousel = new ElementRef(carousel);
    component.startVisitCarousel();
    tick(4999);
    expect(scroll).not.toHaveBeenCalled();
    tick(1);
    expect(scroll).toHaveBeenCalledWith({ left: 300, behavior: 'auto' });
    carousel.scrollLeft = 600;
    tick(5000);
    expect(reset).toHaveBeenCalledWith({ left: 0, behavior: 'instant' });
    carousel.scrollLeft = 0;
    tick(5000);
    expect(scroll).toHaveBeenCalledTimes(2);
    component.ngOnDestroy();
    tick(5000);
    expect(scroll).toHaveBeenCalledTimes(2);
  }));

  it('keeps a single artwork slide stationary', () => {
    const carousel = document.createElement('div');
    const slide = document.createElement('div');
    slide.className = 'visit-art-slide';
    carousel.appendChild(slide);
    Object.defineProperty(carousel, 'scrollWidth', { value: 300 });
    Object.defineProperty(carousel, 'clientWidth', { value: 300 });
    carousel.scrollBy = jasmine.createSpy('scrollBy');
    spyOn(carousel, 'matches').and.returnValue(false);
    spyOnProperty(document, 'hidden', 'get').and.returnValue(false);
    spyOn(window, 'matchMedia').and.returnValue(
      jasmine.createSpyObj<MediaQueryList>('media', ['addEventListener'], { matches: false })
    );
    const component: HomeComponent = Object.create(HomeComponent.prototype);
    component.visitCarousel = new ElementRef(carousel);
    component.advanceVisitArtwork();
    expect(carousel.scrollBy).not.toHaveBeenCalled();
  });

  it('does not auto-advance when hovered, focused, hidden or reduced motion is requested', () => {
    const carousel = document.createElement('div');
    const card = document.createElement('button');
    card.className = 'special-tile';
    carousel.appendChild(card);
    document.body.appendChild(carousel);
    Object.defineProperty(carousel, 'scrollWidth', { value: 600 });
    Object.defineProperty(carousel, 'clientWidth', { value: 200 });
    const scroll = jasmine.createSpy('scrollBy');
    carousel.scrollBy = scroll;
    const hover = spyOn(carousel, 'matches').and.returnValue(true);
    const hidden = spyOnProperty(document, 'hidden', 'get').and.returnValue(false);
    const media = jasmine.createSpyObj<MediaQueryList>('media', ['addEventListener'], { matches: false });
    const matchMedia = spyOn(window, 'matchMedia').and.returnValue(media);
    const component: HomeComponent = Object.create(HomeComponent.prototype);
    component.specialsCarousel = new ElementRef(carousel);
    component.categoryCarousel = new ElementRef(carousel);
    component.visitCarousel = new ElementRef(carousel);
    component.advanceSpecials();
    component.advanceCategories();
    component.advanceVisitArtwork();
    hover.and.returnValue(false);
    card.focus();
    component.advanceSpecials();
    component.advanceCategories();
    component.advanceVisitArtwork();
    card.blur();
    hidden.and.returnValue(true);
    component.advanceSpecials();
    component.advanceCategories();
    component.advanceVisitArtwork();
    hidden.and.returnValue(false);
    const reduced = jasmine.createSpyObj<MediaQueryList>('reduced', ['addEventListener'], { matches: true });
    matchMedia.and.returnValue(reduced);
    component.advanceSpecials();
    component.advanceCategories();
    component.advanceVisitArtwork();
    expect(scroll).not.toHaveBeenCalled();
    matchMedia.and.returnValue(media);
    component.activePanel = 'contact';
    component.advanceCategories();
    component.advanceVisitArtwork();
    expect(scroll).not.toHaveBeenCalled();
    carousel.remove();
  });

  it('opens separate panels, locks background scrolling and restores focus on close', () => {
    const trigger = document.createElement('button');
    const dialog = document.createElement('dialog');
    dialog.innerHTML = '<button autofocus>Close</button>';
    document.body.append(trigger, dialog);
    trigger.focus();
    const originalOverflow = document.body.style.overflow;
    const component: HomeComponent = Object.create(HomeComponent.prototype);
    Object.assign(component, { activePanel: null });
    component.homeDialog = new ElementRef(dialog);
    const refresh = spyOn(component, 'loadPublicStats');
    component.openHomePanel('story');
    expect(refresh).toHaveBeenCalledTimes(1);
    expect(dialog.open).toBeTrue();
    expect(component.activePanel).toBe('story');
    expect(document.body.style.overflow).toBe('hidden');
    component.openHomePanel('offers');
    expect(component.activePanel).toBe('offers');
    component.openHomePanel('rewards');
    expect(component.activePanel).toBe('rewards');
    component.openHomePanel('contact');
    expect(component.activePanel).toBe('contact');
    expect(refresh).toHaveBeenCalledTimes(1);
    component.closeHomePanel();
    expect(dialog.open).toBeFalse();
    expect(component.activePanel).toBeNull();
    expect(document.body.style.overflow).toBe(originalOverflow);
    expect(document.activeElement).toBe(trigger);
    component.openHomePanel('story');
    expect(refresh).toHaveBeenCalledTimes(2);
    component.closeHomePanel();
    trigger.remove();
    dialog.remove();
  });

  it('closes on backdrop clicks but not clicks inside a panel, and cleans up on destruction', () => {
    const dialog = document.createElement('dialog');
    const content = document.createElement('div');
    dialog.appendChild(content);
    document.body.appendChild(dialog);
    const component: HomeComponent = Object.create(HomeComponent.prototype);
    Object.assign(component, { activePanel: null });
    component.homeDialog = new ElementRef(dialog);
    spyOn(component, 'loadPublicStats');
    dialog.addEventListener('click', event => component.onPanelBackdropClick(event));
    component.openHomePanel('story');
    content.click();
    expect(dialog.open).toBeTrue();
    dialog.click();
    expect(dialog.open).toBeFalse();
    component.openHomePanel('contact');
    component.ngOnDestroy();
    expect(dialog.open).toBeFalse();
    expect(component.activePanel).toBeNull();
    dialog.remove();
  });

  it('uses real public counts and the supplied Zomato rating', () => {
    const data = { totalOrders: 5076, menuItemCount: 323, fiveStarReviewCount: 88, yearsServing: 1 };
    const component: HomeComponent = Object.create(HomeComponent.prototype);
    Object.assign(component, { http: { get: () => of({ success: true, data }) }, zomatoRating: 4.2 });
    component.loadPublicStats();
    expect(component.storyStats).toEqual(data);
    expect(component.storyStatsLoading).toBeFalse();
    expect(component.storyStatsError).toBe('');
    expect(component.zomatoRating).toBe(4.2);
  });

  it('shows an explicit error when story statistics fail to load', () => {
    const component: HomeComponent = Object.create(HomeComponent.prototype);
    Object.assign(component, { http: { get: () => throwError(() => new Error('network failure')) } });
    spyOn(console, 'error');
    component.loadPublicStats();
    expect(component.storyStatsLoading).toBeFalse();
    expect(component.storyStatsError).toContain('Could not load');
    expect(console.error).toHaveBeenCalled();
  });

  it('requests fresh counts and replaces them with updated API values', () => {
    const first = { totalOrders: 10, menuItemCount: 20, fiveStarReviewCount: 3, yearsServing: 1 };
    const second = { ...first, totalOrders: 11, menuItemCount: 21, fiveStarReviewCount: 4 };
    const get = jasmine.createSpy('get').and.returnValues(
      of({ success: true, data: first }), of({ success: true, data: second })
    );
    const component: HomeComponent = Object.create(HomeComponent.prototype);
    Object.assign(component, { http: { get }, zomatoRating: 4.2 });
    component.loadPublicStats();
    expect(component.storyStats).toEqual(first);
    component.loadPublicStats();
    expect(component.storyStats).toEqual(second);
    expect(component.zomatoRating).toBe(4.2);
    expect(get.calls.mostRecent().args[1]).toEqual({
      headers: { 'ngsw-bypass': 'true', 'Cache-Control': 'no-cache' }
    });
  });
});
