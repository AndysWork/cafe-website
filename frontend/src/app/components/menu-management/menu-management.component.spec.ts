import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { EMPTY, of } from 'rxjs';
import { MenuManagementComponent } from './menu-management.component';
import { MenuService } from '../../services/menu.service';
import { OutletService } from '../../services/outlet.service';
import { UIStore } from '../../store/ui.store';

describe('MenuManagementComponent category labels', () => {
  let fixture: ComponentFixture<MenuManagementComponent>;
  let component: MenuManagementComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [MenuManagementComponent],
      providers: [
        provideHttpClient(),
        { provide: OutletService, useValue: { selectedOutlet$: EMPTY, getSelectedOutlet: () => null } },
        { provide: MenuService, useValue: { menuItemsRefresh$: of(false) } },
        { provide: UIStore, useValue: {} }
      ]
    });
    fixture = TestBed.createComponent(MenuManagementComponent);
    component = fixture.componentInstance;
    component.categories = [{ id: 'category-1', name: 'Breakfast &amp; Wellness' }];
    component.subCategories = [{
      id: 'subcategory-1', categoryId: 'category-1', name: 'Eggs &amp;amp; Toast'
    }];
    component.menuItems = [{
      id: 'item-1',
      name: 'Chicken Ham [2Pcs]',
      description: '',
      category: 'Breakfast &amp; Wellness',
      categoryId: 'category-1',
      subCategoryId: 'subcategory-1',
      quantity: 1,
      makingPrice: 0,
      packagingCharge: 0,
      shopSellingPrice: 59,
      onlinePrice: 59,
      webPrice: 59,
      variants: [],
      createdBy: 'Admin',
      createdDate: '',
      lastUpdatedBy: 'Admin',
      lastUpdated: ''
    }];
    fixture.detectChanges();
  });

  it('decodes category pills, accordion headings, and filter options', () => {
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelector('.cat-pill')?.textContent).toBe('Breakfast & Wellness');
    expect(root.querySelector('.admin-category-title')?.textContent).toBe('Breakfast & Wellness');
    expect(root.querySelector('.admin-subcategory-title')?.textContent).toBe('Eggs & Toast');
    expect(root.querySelector('.cat-select option[value="category-1"]')?.textContent?.trim())
      .toBe('Breakfast & Wellness');
  });

  it('decodes edit dropdown labels without changing stored names or IDs', async () => {
    component.openEditModal(component.menuItems[0]);
    fixture.detectChanges();
    await fixture.whenStable();

    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelector('select[name="categoryId"] option[value="category-1"]')?.textContent?.trim())
      .toBe('Breakfast & Wellness');
    expect(root.querySelector('select[name="subCategoryId"] option[value="subcategory-1"]')?.textContent?.trim())
      .toBe('Eggs & Toast');
    expect(component.formData.category).toBe('Breakfast &amp; Wellness');
    expect(component.formData.categoryId).toBe('category-1');
    expect(component.formData.subCategoryId).toBe('subcategory-1');
  });

  it('keeps decoded markup as plain text rather than rendering HTML', () => {
    component.menuItems[0].category = '&lt;img src=x onerror=alert(1)&gt; &amp; Wellness';
    fixture.detectChanges();

    const pill: HTMLElement = fixture.nativeElement.querySelector('.cat-pill');
    expect(pill.textContent).toBe('<img src=x onerror=alert(1)> & Wellness');
    expect(pill.querySelector('img')).toBeNull();
  });

  it('preserves ordinary labels and handles missing labels', () => {
    expect(component.getDisplayText('Breakfast & Wellness')).toBe('Breakfast & Wellness');
    expect(component.getDisplayText(undefined)).toBe('');
  });
});
